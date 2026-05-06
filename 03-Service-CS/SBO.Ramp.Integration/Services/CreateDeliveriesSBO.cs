using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using SBO.Ramp.Integration.Data;
using SBO.Ramp.Integration.Models;
using SAPbobsCOM;
using NLog;
using System.Runtime.InteropServices;
using Dapper;

namespace SBO.Ramp.Integration.Services
{
    public class CreateDeliveriesSBO
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private Company _oCompany;
        private readonly RampRepository _repo;
        private readonly SapManager _sapManager;
        private readonly string _modoProceso;
        private readonly string _fuenteRAMP;

        public CreateDeliveriesSBO()
        {
            _repo = new RampRepository();
            _sapManager = new SapManager();
            _modoProceso = ConfigurationManager.AppSettings["ModoProceso"]?.ToUpper() ?? "TEST";
            _fuenteRAMP = ConfigurationManager.AppSettings["FuenteRAMP"]?.ToUpper() ?? "BD";
        }

        public void ProcesarOrdenesCerradas()
        {
            try
            {
                Logger.Info($"Iniciando ciclo de integración. Modo: [{_modoProceso}], FuenteRAMP: [{_fuenteRAMP}]");

                var cola = _repo.ObtenerOrdenesCerradas();
                if (cola == null || !cola.Any())
                {
                    Logger.Info("No hay registros pendientes en la cola.");
                    return;
                }

                var listaActivas = (ConfigurationManager.AppSettings["SociedadesActivas"] ?? "")
                                    .Split(',')
                                    .Select(s => s.Trim().ToUpper())
                                    .ToList();

                var gruposPorEmpresa = cola.GroupBy(x => x.U_Empresa.Trim().ToUpper());

                foreach (var grupo in gruposPorEmpresa)
                {
                    string prefijo = grupo.Key;
                    if (!listaActivas.Contains(prefijo)) continue;

                    _oCompany = _sapManager.Conectar(prefijo);
                    if (_oCompany != null && _oCompany.Connected)
                    {
                        try
                        {
                            foreach (var item in grupo)
                            {
                                if (item.U_Action?.ToUpper() == "CANCEL")
                                {
                                    AnularEntrega(item);
                                }
                                else
                                {
                                    ProcesarDocumento(item);
                                }
                            }
                        }
                        finally
                        {
                            _sapManager.Desconectar(_oCompany);
                        }
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
                Logger.Info($"[CREATE] Procesando: {rampId}");

                // Obtener la orden (desde Mock o BD según configuración)
                ShipmentOrder rampOrder = _repo.ObtenerOrdenCompleta(rampId, _oCompany.CompanyDB, _oCompany.CompanyDB);

                if (rampOrder == null)
                {
                    _repo.ActualizarResultado(queueItem.ID, "E", 0, 0, "No se pudo obtener la orden desde RAMP o no se encontró en SAP.");
                    return;
                }

                if (rampOrder.Detalles == null || !rampOrder.Detalles.Any())
                {
                    _repo.ActualizarResultado(queueItem.ID, "E", 0, 0, "La orden no contiene detalles/productos.");
                    return;
                }

                int pedidoDocEntry = rampOrder.SapOrderDocEntry;

                if (pedidoDocEntry <= 0)
                {
                    Logger.Error($"[CREATE] DocEntry inválido: {pedidoDocEntry}");
                    _repo.ActualizarResultado(queueItem.ID, "E", 0, 0, $"DocEntry inválido: {pedidoDocEntry}");
                    return;
                }

                Logger.Info($"[VALIDACIÓN] Pedido DocEntry={pedidoDocEntry}, OrderNumber={rampOrder.OrderNumber}, Cliente={rampOrder.SapCardCode}");

                oDelivery = CrearObjetoEntrega(pedidoDocEntry, rampOrder, rampId);

                if (oDelivery == null)
                {
                    _repo.ActualizarResultado(queueItem.ID, "E", 0, 0, "Error al crear objeto entrega en memoria.");
                    return;
                }

                bool homologacionExitosa = HomologarCantidadesConRAMP(oDelivery, rampOrder);

                if (!homologacionExitosa)
                {
                    _repo.ActualizarResultado(queueItem.ID, "E", 0, 0, "Error al homologar cantidades - validación fallida.");
                    return;
                }

                if (!ValidarEntregaTieneAlMenosUnaLinea(oDelivery))
                {
                    Logger.Warn($"[CREATE] {rampId} - Todas las cantidades son 0. No se crea entrega.");
                    _repo.ActualizarResultado(queueItem.ID, "X", 0, 0, "Sin productos por entregar (todas cantidades 0).");
                    return;
                }

                // ⭐ MODIFICADO: Log más descriptivo según el modo
                if (_modoProceso == "TEST")
                {
                    Logger.Info($"[SIMULACIÓN] Entrega para {rampId} - NO se grabó en SAP. Modo={_modoProceso}, Fuente={_fuenteRAMP}");
                    LogEstadoEntregaEnMemoria(oDelivery);
                    _repo.ActualizarResultado(queueItem.ID, "C", 0, 0, $"Simulación exitosa - Fuente: {_fuenteRAMP}");
                    return;
                }

                // Modo PRD - grabar en SAP
                Logger.Info($"[PRD] Grabando entrega en SAP para RAMP ID: {rampId}. Fuente de datos: {_fuenteRAMP}");
                int status = oDelivery.Add();

                if (status == 0)
                {
                    string newKey = _oCompany.GetNewObjectKey();
                    int docEntry = int.Parse(newKey);
                    Logger.Info($"[ÉXITO] Entrega {docEntry} creada para RAMP ID: {rampId}");
                    _repo.ActualizarResultado(queueItem.ID, "C", docEntry, docEntry, $"Sincronizado con RAMP. Fuente: {_fuenteRAMP}");
                }
                else
                {
                    string errorMsg = _oCompany.GetLastErrorDescription();
                    int errorCode = _oCompany.GetLastErrorCode();
                    Logger.Error($"[ERROR] Falló creación entrega {rampId}: Código={errorCode}, Mensaje={errorMsg}");
                    _repo.ActualizarResultado(queueItem.ID, "E", 0, 0, $"Error SAP: {errorCode} - {errorMsg}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error creando entrega {rampId}");
                _repo.ActualizarResultado(queueItem.ID, "E", 0, 0, ex.Message);
            }
            finally
            {
                if (oDelivery != null) Marshal.ReleaseComObject(oDelivery);
            }
        }

        private Documents CrearObjetoEntrega(int pedidoDocEntry, ShipmentOrder rampOrder, string rampId)
        {
            Documents oDelivery = null;
            Documents oPedido = null;

            try
            {
                oDelivery = (Documents)_oCompany.GetBusinessObject(BoObjectTypes.oDeliveryNotes);
                oPedido = (Documents)_oCompany.GetBusinessObject(BoObjectTypes.oOrders);

                oDelivery.DocDate = DateTime.Now;
                oDelivery.Comments = $"RAMP Int. | Ref: {rampOrder.OrderNumber}";
                oDelivery.CardCode = rampOrder.SapCardCode;
                oDelivery.DocType = BoDocumentTypes.dDocument_Items;

                if (!oPedido.GetByKey(pedidoDocEntry))
                {
                    Logger.Error($"[MEMORIA] No se pudo obtener el pedido con DocEntry: {pedidoDocEntry}");
                    return null;
                }

                int lineasPedido = oPedido.Lines.Count;
                Logger.Info($"[MEMORIA] Pedido base encontrado: DocEntry={pedidoDocEntry}, Líneas={lineasPedido}");

                if (lineasPedido == 0)
                {
                    Logger.Warn("[MEMORIA] ⚠️ El pedido base no tiene líneas.");
                    return oDelivery;
                }

                for (int i = 0; i < lineasPedido; i++)
                {
                    oPedido.Lines.SetCurrentLine(i);

                    if (i > 0)
                    {
                        oDelivery.Lines.Add();
                    }

                    oDelivery.Lines.SetCurrentLine(i);
                    oDelivery.Lines.ItemCode = oPedido.Lines.ItemCode;
                    oDelivery.Lines.Quantity = oPedido.Lines.Quantity;
                    oDelivery.Lines.UnitPrice = oPedido.Lines.UnitPrice;

                    oDelivery.Lines.BaseType = (int)BoObjectTypes.oOrders;
                    oDelivery.Lines.BaseEntry = pedidoDocEntry;
                    oDelivery.Lines.BaseLine = i;

                    // ⭐ COPIAR DIMENSIONES (Centro de Costo, Proyecto, etc.)
                    // Dimensión 1 (generalmente Centro de Costo)
                    oDelivery.Lines.CostingCode = oPedido.Lines.CostingCode;


                    // ⭐ DIMENSIÓN 2 (actualmente NULL) - Asignar valor por defecto 2000
                    try
                    {
                        string dim2Value = oPedido.Lines.CostingCode2;
                        if (string.IsNullOrEmpty(dim2Value))
                        {
                            // Asignar valor por defecto 2000 (existe en el sistema)
                            dim2Value = "2000";
                            Logger.Warn($"[MEMORIA] Línea {i}: CostingCode2 vacío, asignando default: {dim2Value}");
                        }
                        oDelivery.Lines.CostingCode2 = dim2Value;
                        Logger.Info($"[MEMORIA] Línea {i}: CostingCode2 = {dim2Value}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"[MEMORIA] Error asignando CostingCode2: {ex.Message}");
                    }










                    //string dim2 = oPedido.Lines.CostingCode2;

                    //oDelivery.Lines.CostingCode2 = oPedido.Lines.CostingCode2;

                    //oDelivery.Lines.CostingCode2 = "001";
                    //oDelivery.Lines.CostingCode3 = oPedido.Lines.CostingCode3;
                    //oDelivery.Lines.CostingCode4 = oPedido.Lines.CostingCode4;
                    //oDelivery.Lines.CostingCode5 = oPedido.Lines.CostingCode5;

                    // Proyecto (si aplica)
                    oDelivery.Lines.ProjectCode = oPedido.Lines.ProjectCode;

                    Logger.Debug($"[MEMORIA] Línea {i}: Producto={oPedido.Lines.ItemCode}, Cantidad={oPedido.Lines.Quantity}");
                    Logger.Debug($"[MEMORIA] Dimensiones: CostingCode={oPedido.Lines.CostingCode}, ProjectCode={oPedido.Lines.ProjectCode}");
                }

                Logger.Info($"[MEMORIA] ✅ Se copiaron {lineasPedido} líneas del pedido a la entrega");

                return oDelivery;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error creando objeto entrega");
                return null;
            }
            finally
            {
                if (oPedido != null) Marshal.ReleaseComObject(oPedido);
            }
        }

        private bool HomologarCantidadesConRAMP(Documents oDelivery, ShipmentOrder rampOrder)
        {
            try
            {
                var cantidadesRAMP = rampOrder.Detalles.ToDictionary(
                    d => d.WarehouseSku,
                    d => new { d.QtyShipped, d.SapOrderLineNum }
                );

                Logger.Info("[HOMOLOGACIÓN] Iniciando ajuste de cantidades con datos de RAMP");
                Logger.Info($"[RAMP] Productos a despachar: {string.Join(", ", cantidadesRAMP.Where(kv => kv.Value.QtyShipped > 0).Select(kv => $"{kv.Key}:{kv.Value.QtyShipped}"))}");

                int lineasCount = oDelivery.Lines.Count;
                List<int> lineasAEliminar = new List<int>();
                int lineasSinCambios = 0;
                int lineasAjustadas = 0;
                int lineasEliminadas = 0;

                for (int i = 0; i < lineasCount; i++)
                {
                    oDelivery.Lines.SetCurrentLine(i);
                    string itemCode = oDelivery.Lines.ItemCode;
                    double cantidadPedido = oDelivery.Lines.Quantity;

                    Logger.Debug($"[LÍNEA {i}] Producto: {itemCode}, Cantidad pedido: {cantidadPedido}");

                    if (cantidadesRAMP.TryGetValue(itemCode, out var rampData))
                    {
                        int cantidadReal = rampData.QtyShipped;

                        if (cantidadReal == 0)
                        {
                            Logger.Info($"[HOMOLOGACIÓN] ⚠️ Producto {itemCode}: RAMP CERO ({cantidadReal}) vs Pedido ({cantidadPedido}) → ELIMINADO");
                            lineasAEliminar.Add(i);
                            lineasEliminadas++;
                        }
                        else if (cantidadReal > cantidadPedido)
                        {
                            Logger.Error($"[ERROR] Producto {itemCode}: RAMP {cantidadReal} > Pedido {cantidadPedido}. EXCEDE.");
                            return false;
                        }
                        else if (cantidadReal != cantidadPedido)
                        {
                            double diferencia = cantidadPedido - cantidadReal;
                            Logger.Info($"[HOMOLOGACIÓN] 🔄 Producto {itemCode}: Pedido: {cantidadPedido} → RAMP: {cantidadReal} (Diferencia: -{diferencia})");
                            oDelivery.Lines.Quantity = cantidadReal;
                            lineasAjustadas++;
                        }
                        else
                        {
                            Logger.Info($"[HOMOLOGACIÓN] ✅ Producto {itemCode}: SIN DIFERENCIAS - {cantidadPedido} = {cantidadReal}");
                            lineasSinCambios++;
                        }
                    }
                    else
                    {
                        Logger.Warn($"[HOMOLOGACIÓN] ⚠️ Producto {itemCode}: NO ENCONTRADO en RAMP → ELIMINADO");
                        lineasAEliminar.Add(i);
                        lineasEliminadas++;
                    }
                }

                var productosEnSAP = new HashSet<string>();
                for (int i = 0; i < lineasCount; i++)
                {
                    oDelivery.Lines.SetCurrentLine(i);
                    productosEnSAP.Add(oDelivery.Lines.ItemCode);
                }

                var productosExtra = cantidadesRAMP.Keys.Where(p => !productosEnSAP.Contains(p)).ToList();
                int productosExtraCount = 0;
                foreach (var productoExtra in productosExtra)
                {
                    var cantidadExtra = cantidadesRAMP[productoExtra];
                    if (cantidadExtra.QtyShipped > 0)
                    {
                        Logger.Warn($"[HOMOLOGACIÓN] ⚠️ Producto {productoExtra} (Cantidad RAMP: {cantidadExtra.QtyShipped}) NO EXISTE en SAP → IGNORADO");
                        productosExtraCount++;
                    }
                }

                for (int i = lineasAEliminar.Count - 1; i >= 0; i--)
                {
                    oDelivery.Lines.SetCurrentLine(lineasAEliminar[i]);
                    oDelivery.Lines.Delete();
                    Logger.Info($"[HOMOLOGACIÓN] 🗑️ Línea {lineasAEliminar[i]} eliminada");
                }

                Logger.Info("[HOMOLOGACIÓN] ========== RESUMEN FINAL ==========");
                Logger.Info($"[HOMOLOGACIÓN] 📊 Líneas sin cambios: {lineasSinCambios}");
                Logger.Info($"[HOMOLOGACIÓN] 📊 Líneas ajustadas: {lineasAjustadas}");
                Logger.Info($"[HOMOLOGACIÓN] 📊 Líneas eliminadas: {lineasEliminadas}");
                Logger.Info($"[HOMOLOGACIÓN] 📊 Productos RAMP ignorados: {productosExtraCount}");
                Logger.Info($"[HOMOLOGACIÓN] 📊 Líneas restantes en entrega: {oDelivery.Lines.Count}");
                Logger.Info("[HOMOLOGACIÓN] ====================================");

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error durante la homologación de cantidades");
                return false;
            }
        }

        private bool ValidarEntregaTieneAlMenosUnaLinea(Documents oDelivery)
        {
            try
            {
                int lineasCount = oDelivery.Lines.Count;
                for (int i = 0; i < lineasCount; i++)
                {
                    oDelivery.Lines.SetCurrentLine(i);
                    if (oDelivery.Lines.Quantity > 0)
                    {
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error validando líneas de entrega");
                return false;
            }
        }

        private void LogEstadoEntregaEnMemoria(Documents oDelivery)
        {
            try
            {
                Logger.Info("[SIMULACIÓN] Estado final del objeto entrega en memoria:");
                int lineasCount = oDelivery.Lines.Count;
                for (int i = 0; i < lineasCount; i++)
                {
                    oDelivery.Lines.SetCurrentLine(i);
                    Logger.Info($"  Línea {i}: Producto={oDelivery.Lines.ItemCode}, Cantidad={oDelivery.Lines.Quantity}");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Error al logear estado: {ex.Message}");
            }
        }

        private void AnularEntrega(ZRampQueue queueItem)
        {
            Documents oDelivery = null;
            string rampId = queueItem.U_RampID;

            try
            {
                Logger.Info($"[CANCEL] Solicitud de reverso Orden: {rampId}. Modo={_modoProceso}, Fuente={_fuenteRAMP}");

                if (!int.TryParse(rampId, out int docEntrySAP))
                {
                    Logger.Error($"[CANCEL] El RampID '{rampId}' no es un número válido de DocEntry.");
                    _repo.ActualizarResultado(queueItem.ID, "E", 0, 0, "RampID inválido para cancelación.");
                    return;
                }

                Logger.Info($"[CANCEL] Buscando entrega con DocEntry: {docEntrySAP}");

                oDelivery = (Documents)_oCompany.GetBusinessObject(BoObjectTypes.oDeliveryNotes);

                if (oDelivery.GetByKey(docEntrySAP))
                {
                    if (oDelivery.DocumentStatus == BoStatus.bost_Close)
                    {
                        Logger.Warn($"[ALERTA] {rampId} ya se encuentra CERRADO en SAP (DocEntry: {docEntrySAP}).");
                        _repo.ActualizarResultado(queueItem.ID, "W", docEntrySAP, 0, "Alerta: Entrega ya procesada/facturada.");
                    }
                    else
                    {
                        if (_modoProceso == "PRD")
                        {
                            int res = oDelivery.Cancel();
                            if (res == 0)
                            {
                                Logger.Info($"[ÉXITO] {rampId} anulado correctamente en SAP.");
                                _repo.ActualizarResultado(queueItem.ID, "A", docEntrySAP, 0, "Documento Anulado en SAP.");
                            }
                            else
                            {
                                string error = _oCompany.GetLastErrorDescription();
                                int errorCode = _oCompany.GetLastErrorCode();
                                Logger.Error($"[ERROR] Falló anulación SAP {rampId}: Código={errorCode}, Mensaje={error}");
                                _repo.ActualizarResultado(queueItem.ID, "E", docEntrySAP, 0, error);
                            }
                        }
                        else
                        {
                            Logger.Info($"[SIMULACIÓN] {rampId} sería anulado en modo {_modoProceso}.");
                            _repo.ActualizarResultado(queueItem.ID, "A", docEntrySAP, 0, $"Simulación - Documento sería anulado. Fuente: {_fuenteRAMP}");
                        }
                    }
                }
                else
                {
                    Logger.Warn($"[CANCEL] No se encontró entrega con DocEntry {docEntrySAP}");
                    _repo.ActualizarResultado(queueItem.ID, "E", 0, 0, $"No se encontró la entrega con DocEntry {docEntrySAP}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error en proceso de cancelación {rampId}");
                _repo.ActualizarResultado(queueItem.ID, "E", 0, 0, ex.Message);
            }
            finally
            {
                if (oDelivery != null) Marshal.ReleaseComObject(oDelivery);
            }
        }
    }
}