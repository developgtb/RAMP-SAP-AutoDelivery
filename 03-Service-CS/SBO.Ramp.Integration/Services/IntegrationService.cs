using SBO.Ramp.Integration.Services;
using System;
using System.Configuration;
using System.ServiceProcess;
using System.Timers;
using NLog;

namespace SBO.Ramp.Integration
{
    public class IntegrationService : ServiceBase
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private Timer _timer;
        private readonly int _intervalo;
        private readonly string _modoProceso;

        public IntegrationService()
        {
            this.ServiceName = "SBO_Ramp_Integration";

            // 1. Lectura del intervalo
            if (!int.TryParse(ConfigurationManager.AppSettings["IntervaloSegundos"], out _intervalo) || _intervalo <= 0)
            {
                _intervalo = 90;
            }

            // 2. Lectura del Modo de Proceso (TEST por defecto para seguridad)
            _modoProceso = ConfigurationManager.AppSettings["ModoProceso"]?.ToUpper() ?? "TEST";
        }

        public void StartDebug() => OnStart(null);
        public void StopDebug() => OnStop();

        protected override void OnStart(string[] args)
        {
            // Banner de seguridad en el log
            Logger.Info("====================================================================");
            Logger.Info($"   INICIANDO SERVICIO: {this.ServiceName}");
            Logger.Info($"   INTERVALO: {_intervalo} segundos");

            if (_modoProceso == "TEST")
            {
                Logger.Warn("   MODO ACTUAL: [ TEST ] - SIMULACIÓN ACTIVA");
                Logger.Warn("   NO SE CREARÁN DOCUMENTOS EN SAP NI SE ACTUALIZARÁ EL BUFFER.");
            }
            else
            {
                Logger.Info("   MODO ACTUAL: [ PRD ] - EJECUCIÓN REAL");
            }
            Logger.Info("====================================================================");

            try
            {
                _timer = new Timer(_intervalo * 1000);
                _timer.Elapsed += OnTimerElapsed;
                _timer.AutoReset = true;
                _timer.Enabled = true;

                Logger.Debug("Lanzando primer ciclo de ejecución inmediata...");
                OnTimerElapsed(null, null);
            }
            catch (Exception ex)
            {
                Logger.Fatal(ex, "Error fatal al inicializar el Timer del servicio.");
                throw;
            }
        }

        private void OnTimerElapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                _timer.Stop();

                // Añadimos el modo al mensaje de inicio de ciclo para claridad total
                Logger.Info($"--- Iniciando ciclo de integración [{_modoProceso}] ---");

                var orchestrator = new IntegrationOrchestrator();
                orchestrator.RunAllProcesses();

                Logger.Info($"--- Ciclo de integración finalizado [{_modoProceso}] ---");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error no controlado en el ciclo del Timer.");
            }
            finally
            {
                if (_timer != null)
                {
                    //_timer.Start();
                }
            }
        }

        protected override void OnStop()
        {
            Logger.Warn($"Recibida instrucción de detener el servicio. Modo actual: {_modoProceso}");

            try
            {
                if (_timer != null)
                {
                    _timer.Stop();
                    _timer.Dispose();
                    _timer = null;
                }
                Logger.Info("Servicio detenido correctamente.");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error al intentar detener el servicio limpiamente.");
            }
        }
    }
}