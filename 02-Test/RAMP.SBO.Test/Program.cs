using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data.SqlClient;
using SAPbobsCOM;

namespace RAMP.SBO.Test
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== DIAGNÓSTICO DE CONEXIONES ===");

            // 1. PROBAR SQL (RAMP)
            ProbarSQL();

            Console.WriteLine("\n---------------------------------\n");

            // 2. PROBAR SAP DI API
            ProbarSAP();

            Console.WriteLine("\nPresiona cualquier tecla para salir...");
            Console.ReadKey();
        }


        static void ProbarSQL()
        {
            Console.WriteLine("[SQL] Iniciando prueba...");
            // Reemplaza con tus datos reales
            string connString = "Data Source=172.31.21.148;Initial Catalog=zDEMO_PINEAPPLE;User ID=carlosmarlos;Password=GTBSAP#2026";

            string sql = "SELECT COUNT(*) FROM [10.1.1.114].[DB_RAMP_BUFF].[dbo].[ZRAMP_QUEUE] WHERE U_Status = 'P'";

            using (SqlConnection conn = new SqlConnection(connString))
            {
                try
                {
                    Console.WriteLine("Conectando al servidor de SAP...");
                    conn.Open();

                    SqlCommand cmd = new SqlCommand(sql, conn);

                    Console.WriteLine("Ejecutando consulta sobre el Linked Server...");
                    // ExecuteScalar es ideal cuando solo esperas un número (el COUNT)
                    int pendientes = (int)cmd.ExecuteScalar();

                    Console.WriteLine("--------------------------------------------------");
                    Console.WriteLine("¡RESULTADO EXITOSO!");
                    Console.WriteLine($"Registros pendientes encontrados: {pendientes}");
                    Console.WriteLine("--------------------------------------------------");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("\n[ERROR DE CONEXIÓN]");
                    Console.WriteLine(ex.Message);

                    if (ex.Message.Contains("Login failed"))
                    {
                        Console.WriteLine("Tip: Revisa que el usuario de SQL tenga permisos en el Linked Server.");
                    }
                }
            }
        }

        static void ProbarSAP()
        {
            Console.WriteLine("[SAP] Iniciando prueba de DI API...");
            Company oCompany = new Company();

            try
            {
                // Configuración mínima necesaria
                oCompany.Server = "172.31.21.148";
                //oCompany.LicenseServer = "172.31.26.246:30000";
                oCompany.LicenseServer = "gtb-app:30000";
                oCompany.DbServerType = BoDataServerTypes.dst_MSSQL2019;
                oCompany.CompanyDB = "zDEMO_PINEAPPLE";
                oCompany.UserName = "manager";
                //oCompany.UserName = "B1SiteUser";
                oCompany.Password = "M@ster00";
                //oCompany.Password = "B1.GTB.S@p.#@!";
                oCompany.DbUserName = "carlosmarlos";
                oCompany.DbPassword = "GTBSAP#2026";
                oCompany.UseTrusted = false;

                int result = oCompany.Connect();

                if (result == 0)
                {
                    Console.WriteLine("[SAP] ¡CONEXIÓN EXITOSA!");
                    Console.WriteLine($"[SAP] Empresa: {oCompany.CompanyName}");
                    Console.WriteLine($"[SAP] Versión DI API: {oCompany.Version}");
                    Console.WriteLine($"[SAP] Usuario actual: {oCompany.UserName}");

                    oCompany.Disconnect();
                }
                else
                {
                    string errDescription = oCompany.GetLastErrorDescription();
                    Console.WriteLine($"[SAP] ERROR ({result}): {errDescription}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SAP] ERROR CRÍTICO: {ex.Message}");
            }
        }

    }
}
