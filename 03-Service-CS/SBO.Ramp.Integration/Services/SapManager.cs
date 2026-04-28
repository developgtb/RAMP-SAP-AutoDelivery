using System;
using System.Configuration;
using SAPbobsCOM;
using NLog;
using System.Runtime.InteropServices;

namespace SBO.Ramp.Integration.Services
{
    public class SapManager
    {
        // Instancia del logger para trazar el ciclo de vida de la conexión
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Establece una conexión con la sociedad de SAP indicada.
        /// </summary>
        /// <param name="empresaRef">Prefijo de la empresa en App.config (PIN, GTB, etc.)</param>
        /// <returns>Objeto Company conectado</returns>
        public Company Conectar(string empresaRef)
        {
            Company oCompany = new Company();

            try
            {
                Logger.Info($"[SAP] Iniciando protocolo de conexión para: {empresaRef}");

                // Configuración Maestra del Servidor
                oCompany.Server = ConfigurationManager.AppSettings["SapServer"];
                oCompany.LicenseServer = ConfigurationManager.AppSettings["SapLicenseServer"];
                oCompany.DbServerType = BoDataServerTypes.dst_MSSQL2019;

                // Credenciales de Base de Datos (SQL)
                oCompany.DbUserName = ConfigurationManager.AppSettings["SapDbUser"];
                oCompany.DbPassword = ConfigurationManager.AppSettings["SapDbPass"];

                // Mapeo dinámico por Empresa (Lectura de App.config)
                oCompany.CompanyDB = ConfigurationManager.AppSettings[$"{empresaRef}_DB"];
                oCompany.UserName = ConfigurationManager.AppSettings[$"{empresaRef}_SapUser"];
                oCompany.Password = ConfigurationManager.AppSettings[$"{empresaRef}_SapPass"];

                // Validación preventiva de configuración
                if (string.IsNullOrEmpty(oCompany.CompanyDB))
                {
                    throw new Exception($"La configuración para '{empresaRef}_DB' no existe en el App.config.");
                }

                int result = oCompany.Connect();

                if (result == 0)
                {
                    Logger.Info($"[SAP] Conexión establecida exitosamente con la BD: {oCompany.CompanyDB}");
                    return oCompany;
                }
                else
                {
                    string errorMsg = oCompany.GetLastErrorDescription();
                    int errorCode = oCompany.GetLastErrorCode();

                    Logger.Error($"[SAP] Error de conexión en {empresaRef}: [{errorCode}] {errorMsg}");

                    // Si falla la conexión, liberamos el objeto de memoria inmediatamente
                    Desconectar(oCompany);
                    throw new Exception($"SAP Connection Error: {errorMsg}");
                }
            }
            catch (Exception ex)
            {
                Logger.Fatal(ex, $"[CRÍTICO] Fallo catastrófico al conectar con la sociedad {empresaRef}");
                Desconectar(oCompany);
                throw;
            }
        }

        /// <summary>
        /// Finaliza la sesión de SAP y libera la memoria COM de forma segura.
        /// </summary>
        public void Desconectar(Company oCompany)
        {
            if (oCompany != null)
            {
                try
                {
                    if (oCompany.Connected)
                    {
                        oCompany.Disconnect();
                        Logger.Info($"[SAP] Sesión finalizada para {oCompany.CompanyDB}. Licencia liberada.");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[SAP] Aviso al desconectar: {ex.Message}");
                }
                finally
                {
                    // IMPORTANTE: SAP DI API es un objeto COM. 
                    // Debemos forzar la liberación para evitar procesos "colgados" en el servidor.
                    Marshal.ReleaseComObject(oCompany);
                    oCompany = null;
                    GC.Collect(); // Sugerimos al recolector de basura limpiar objetos COM
                }
            }
        }
    }
}