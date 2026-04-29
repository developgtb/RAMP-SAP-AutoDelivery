using NLog;
using SAPbobsCOM;
using System;
using System.Configuration;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

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
                /* Fuerza al sistema a releer el archivo desde el disco
                ConfigurationManager.RefreshSection("appSettings");

                Logger.Info("--- [DIAGNÓSTICO DE CONFIGURACIÓN] ---");
                Logger.Info($"Archivo cargado: {AppDomain.CurrentDomain.SetupInformation.ConfigurationFile}");

                foreach (string key in ConfigurationManager.AppSettings.AllKeys)
                {
                    Logger.Info($"Llave encontrada: {key} | Valor: {ConfigurationManager.AppSettings[key]}");
                }
                Logger.Info("--- [FIN DIAGNÓSTICO] ---");*/

                string keycia = $"{empresaRef}_DB";
                Logger.Info($"Intentando leer la llave: '{keycia}'");

                string keyuser = $"{empresaRef}_SapUser";
                string keypass = $"{empresaRef}_SapPass";

                string valuecia = ConfigurationManager.AppSettings[keycia];

                if (string.IsNullOrEmpty(valuecia))
                {
                    // Si falla, listamos qué llaves SI existen para comparar
                    string llavesDisponibles = string.Join(", ", ConfigurationManager.AppSettings.AllKeys);
                    Logger.Error($"No se encontró '{keycia}'. Llaves que sí existen en el config: {llavesDisponibles}");
                    return null;
                }

                string valueuser = ConfigurationManager.AppSettings[keyuser];
                string valuepass = ConfigurationManager.AppSettings[keypass];

                //oCompany.CompanyDB = ConfigurationManager.AppSettings[$"{empresaRef}_DB"];
                //oCompany.UserName = ConfigurationManager.AppSettings[$"{empresaRef}_SapUser"];
                //oCompany.Password = ConfigurationManager.AppSettings[$"{empresaRef}_SapPass"];

                oCompany.CompanyDB = valuecia;
                oCompany.UserName = valueuser;
                oCompany.Password = valuepass;

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