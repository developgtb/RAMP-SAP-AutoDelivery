using System;
using System.ServiceProcess;
using NLog;

namespace SBO.Ramp.Integration
{
    internal static class Program
    {
        // Instanciamos el logger para la clase Program
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Punto de entrada principal para la aplicación.
        /// </summary>
        static void Main(string[] args)
        {
            try
            {
                var service = new IntegrationService();

                // Detectamos si se ejecuta como consola (Debug) o como Servicio de Windows
                if (Environment.UserInteractive)
                {
                    EjecutarModoConsola(service);
                }
                else
                {
                    // Registro de inicio en modo Servicio
                    Logger.Info("Iniciando aplicación en modo Servicio de Windows.");
                    ServiceBase.Run(service);
                }
            }
            catch (Exception ex)
            {
                // Este bloque es vital: si el servicio falla al cargar, aquí sabremos por qué
                Logger.Fatal(ex, "La aplicación no pudo iniciar debido a un error crítico.");

                if (Environment.UserInteractive)
                {
                    Console.WriteLine($"\n[FATAL] Error de inicio: {ex.Message}");
                    Console.WriteLine("Presiona cualquier tecla para salir...");
                    Console.ReadKey();
                }
            }
            finally
            {
                // Asegura que todos los logs pendientes se escriban antes de cerrar el proceso
                LogManager.Shutdown();
            }
        }

        private static void EjecutarModoConsola(IntegrationService service)
        {
            Console.Title = "SBO-RAMP Integration | MODO DEBUG";

            Logger.Info("==================================================");
            Logger.Info("    INICIANDO SERVICIO EN MODO INTERACTIVO");
            Logger.Info("==================================================");

            try
            {
                service.StartDebug();

                Logger.Info("[INFO] Sistema en ejecución. Presiona 'Q' para detener el proceso...");

                // Bucle de espera hasta presionar Q
                while (true)
                {
                    if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Q)
                        break;
                }

                Logger.Warn("Deteniendo servicio por solicitud del usuario...");
                service.StopDebug();
                Logger.Info("Proceso finalizado correctamente.");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error durante la ejecución en modo interactivo.");
            }
        }
    }
}