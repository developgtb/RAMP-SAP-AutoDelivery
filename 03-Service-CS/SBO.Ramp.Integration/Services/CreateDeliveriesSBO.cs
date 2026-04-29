using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using SBO.Ramp.Integration.Data;
using SBO.Ramp.Integration.Models;
using SAPbobsCOM;
using NLog;
using System.Runtime.InteropServices;

namespace SBO.Ramp.Integration.Services
{
    public class CreateDeliveriesSBO
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private Company _oCompany;
        private readonly RampRepository _repo;
        private readonly SapManager _sapManager;
        private readonly string _modoProceso;

        public CreateDeliveriesSBO()
        {
            _repo = new RampRepository();
            _sapManager = new SapManager();
            // Leemos el modo una sola vez al instanciar
            _modoProceso = ConfigurationManager.AppSettings["ModoProceso"]?.ToUpper() ?? "TEST";
        }

        public void ProcesarOrdenesCerradas()
        {
            try
            {
                Logger.Info($"Iniciando ciclo de entregas. Modo: [{_modoProceso}]");

                var cerradas = _repo.ObtenerOrdenesCerradas();
                if (cerradas == null || !cerradas.Any())
                {
                    Logger.Info("No hay registros pendientes en la cola.");
                    return;
                }

                Logger.Info($"Registros encontrados: {cerradas.Count}");

                var listaActivas = (ConfigurationManager.AppSettings["SociedadesActivas"] ?? "")
                                    .Split(',')
                                    .Select(s => s.Trim().ToUpper())
                                    .Where(s => !string.IsNullOrEmpty(s))
                                    .ToList();

                var gruposPorEmpresa = cerradas.GroupBy(x => x.U_Empresa.Trim().ToUpper());

                foreach (var grupo in gruposPorEmpresa)
                {
                    string prefijo = grupo.Key;

                    if (!listaActivas.Contains(prefijo))
                    {
                        Logger.Warn($"[Filtro] Sociedad '{prefijo}' no activa. Omitiendo {grupo.Count()} docs.");
                        continue;
                    }

                    Logger.Info($"--- Bloque: {prefijo} ({grupo.Count()} registros) ---");

                    _oCompany = _sapManager.Conectar(prefijo);

                    if (_oCompany != null && _oCompany.Connected)
                    {
                        try
                        {
                            foreach (var ordenEnCola in grupo)
                            {
                                ProcesarDocumento(ordenEnCola);
                            }
                        }
                        finally
                        {
                            _sapManager.Desconectar(_oCompany);
                            _oCompany = null;
                        }
                    }
                    else
                    {
                        Logger.Error($"Error de conexión para {prefijo}. Bloque cancelado.");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Fatal(ex, "Fallo crítico en el orquestador.");
            }
        }

        private void ProcesarDocumento(ZRampQueue queueItem)
        {
            Documents oDelivery = null;
            string rampId = queueItem.U_RampID;

            try
            {
                Logger.Info($"[Doc] Procesando orden: {rampId}");

                ShipmentOrder rampOrder = _repo.ObtenerOrdenCompleta(rampId, _oCompany.CompanyDB);

                if (rampOrder == null || rampOrder.Detalles == null || !rampOrder.Detalles.Any())
                {
                    string msg = "No se encontró el detalle en RAMP o cruce con SAP.";
                    Logger.Warn($"[Doc] {rampId}: {msg}");
                    _repo.ActualizarResultado(queueItem.ID, "E", 0, 0, msg);
                    return;
                }

                oDelivery = (Documents)_oCompany.GetBusinessObject(BoObjectTypes.oDeliveryNotes);

                bool permitirSencilla = bool.TryParse(ConfigurationManager.AppSettings["PermitirEntregaSencilla"], out bool res) && res;
                string whsDefault = ConfigurationManager.AppSettings["DefaultWhs"] ?? "01";

                // Mapeo Cabecera
                oDelivery.CardCode = rampOrder.SapCardCode;
                oDelivery.DocDate = rampOrder.DocumentDate ?? DateTime.Now;
                oDelivery.Comments = $"RAMP Int. [{_modoProceso}] | Ref: {rampOrder.OrderNumber}";
                oDelivery.UserFields.Fields.Item("U_RampID").Value = rampId;

                // Mapeo Líneas
                int lineIdx = 0;
                foreach (var det in rampOrder.Detalles)
                {
                    if (lineIdx > 0) oDelivery.Lines.Add();
                    oDelivery.Lines.SetCurrentLine(lineIdx);

                    if (det.SapOrderDocEntry > 0)
                    {
                        oDelivery.Lines.BaseType = 17;
                        oDelivery.Lines.BaseEntry = det.SapOrderDocEntry;
                        oDelivery.Lines.BaseLine = det.SapOrderLineNum;
                    }
                    else
                    {
                        if (!permitirSencilla)
                            throw new Exception($"Item {det.SapItemCode} sin pedido base (PermitirEntregaSencilla=false).");

                        oDelivery.Lines.ItemCode = det.SapItemCode;
                    }

                    oDelivery.Lines.Quantity = det.QtyShipped;
                    oDelivery.Lines.WarehouseCode = whsDefault;
                    lineIdx++;
                }

                // --- BIFURCACIÓN SEGÚN MODO ---
                if (_modoProceso == "TEST")
                {
                    Logger.Info($"[SIMULACIÓN] {rampId} validado con éxito. (Modo TEST: No se creó en SAP)");
                    // Importante: No llamamos a ActualizarResultado para que el registro siga en 'P' (Pendiente)
                    return;
                }

                // Ejecución Real (PRD)
                int status = oDelivery.Add();

                if (status == 0)
                {
                    string newKey = _oCompany.GetNewObjectKey();
                    Logger.Info($"[ÉXITO] {rampId} -> DocEntry: {newKey}");
                    _repo.ActualizarResultado(queueItem.ID, "C", int.Parse(newKey), int.Parse(newKey), "Sincronizado.");
                }
                else
                {
                    string sapError = _oCompany.GetLastErrorDescription();
                    Logger.Error($"[SAP ERROR] {rampId}: {sapError}");
                    _repo.ActualizarResultado(queueItem.ID, "E", 0, 0, $"SAP: {sapError}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[EXCEPCIÓN] {rampId}");
                // Solo actualizamos a error si no estamos en modo TEST
                if (_modoProceso != "TEST")
                {
                    _repo.ActualizarResultado(queueItem.ID, "E", 0, 0, $"Excepción: {ex.Message}");
                }
            }
            finally
            {
                if (oDelivery != null)
                {
                    Marshal.ReleaseComObject(oDelivery);
                    oDelivery = null;
                }
            }
        }
    }
}