using System;
using NLog;

namespace SBO.Ramp.Integration.Services
{
    public class IntegrationOrchestrator
    {
        // Instancia de Logger para el Orquestador
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public void RunAllProcesses()
        {
            Logger.Info("Iniciando orquestación de procesos de negocio.");

            // 1. PROCESO: RAMP -> SAP BUSINESS ONE
            // Encargado de procesar entregas (Entorno .10 -> SAP .148)
            EjecutarProceso(CrearEntregasSBO, "Integración RAMP-SAP");

            // 2. PROCESO: HANSHOW -> ETIQUETAS ELECTRÓNICAS
            // Próximo despliegue: 300 tiendas / 12,000 productos
            // EjecutarProceso(ActualizarPreciosHanshow, "Actualización Etiquetas Hanshow");

            Logger.Info("Orquestación de procesos finalizada.");
        }

        /// <summary>
        /// Método envoltorio (Wrapper) para ejecutar procesos de forma segura
        /// </summary>
        private void EjecutarProceso(Action proceso, string nombreProceso)
        {
            try
            {
                Logger.Info($"[PROCESO] Iniciando: {nombreProceso}...");
                proceso();
                Logger.Info($"[PROCESO] Finalizado: {nombreProceso} con éxito.");
            }
            catch (Exception ex)
            {
                // El error de un proceso individual no debe tumbar al orquestador
                Logger.Error(ex, $"[PROCESO] Falló: {nombreProceso}. Mensaje: {ex.Message}");
            }
        }

        private void CrearEntregasSBO()
        {
            // Usamos el servicio específico para esta tarea
            var deliveryService = new CreateDeliveriesSBO();
            deliveryService.ProcesarOrdenesCerradas();
        }

        /* private void ActualizarPreciosHanshow()
        {
            // Aquí irá la lógica para los 12,000 productos y 300 tiendas
            // var hanshow = new HanshowPriceService();
            // hanshow.SincronizarTodo();
        } 
        */
    }
}