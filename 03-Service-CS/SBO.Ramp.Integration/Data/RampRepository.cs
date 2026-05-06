using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using Dapper;
using Newtonsoft.Json;
using SBO.Ramp.Integration.Models;
using NLog;

namespace SBO.Ramp.Integration.Data
{
    public class RampRepository
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly string _connectionString;
        private readonly string _linkServer;
        private readonly string _dbBuffer;
        private readonly string _dbReal;
        private readonly string _modoProceso;
        private readonly string _fuenteRAMP;
        private readonly string _mockFilePath;

        public RampRepository()
        {
            _connectionString = ConfigurationManager.ConnectionStrings["SQLSBO"]?.ConnectionString;
            _linkServer = ConfigurationManager.AppSettings["LinkedServerRamp"];
            _dbBuffer = ConfigurationManager.AppSettings["DbBufferRamp"];
            _dbReal = ConfigurationManager.AppSettings["DbRealRamp"];
            _modoProceso = ConfigurationManager.AppSettings["ModoProceso"]?.ToUpper() ?? "TEST";
            _fuenteRAMP = ConfigurationManager.AppSettings["FuenteRAMP"]?.ToUpper() ?? "BD";
            _mockFilePath = ConfigurationManager.AppSettings["MockFilePath"] ?? "MockData\\ejemplo01.json";

            ValidarConfiguracion();
        }

        private void ValidarConfiguracion()
        {
            if (string.IsNullOrEmpty(_connectionString))
                throw new Exception("Falta 'SQLSBO' en connectionStrings.");

            if (_fuenteRAMP == "BD")
            {
                if (string.IsNullOrEmpty(_linkServer))
                    throw new Exception("Falta 'LinkedServerRamp' en appSettings.");
                if (string.IsNullOrEmpty(_dbBuffer))
                    throw new Exception("Falta 'DbBufferRamp' en appSettings.");
                if (string.IsNullOrEmpty(_dbReal))
                    throw new Exception("Falta 'DbRealRamp' en appSettings.");
            }

            // ⭐ CAMBIO: Validar archivo mock solo si FuenteRAMP = MOCK (independientemente del modo)
            if (_fuenteRAMP == "MOCK")
            {
                string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _mockFilePath);
                if (!File.Exists(fullPath))
                {
                    Logger.Error($"[MOCK] ❌ Archivo mock NO ENCONTRADO: {fullPath}");
                    Logger.Error("[MOCK] La ejecución continuará pero no habrá datos que procesar.");
                }
                else
                {
                    Logger.Info($"[MOCK] ✅ Archivo mock encontrado: {fullPath}");
                }
            }

            // ⭐ ELIMINADO: Validación que prohibía MOCK + PRD
        }

        /// <summary>
        /// Determina si debe usar datos Mock (independientemente del modo)
        /// </summary>
        private bool DebeUsarMock()
        {
            // Usar Mock si FuenteRAMP = MOCK (sin importar ModoProceso)
            return _fuenteRAMP == "MOCK";
        }

        /// <summary>
        /// Determina si debe simular la actualización (no escribir en BD)
        /// Solo cuando ModoProceso = TEST, independientemente de la fuente
        /// </summary>
        private bool DebeSimularActualizacion()
        {
            // Solo simular (no actualizar BD) cuando ModoProceso = TEST
            return _modoProceso == "TEST";
        }

        public List<ZRampQueue> ObtenerOrdenesCerradas()
        {
            if (DebeUsarMock())
            {
                Logger.Info("[MOCK] Generando lista mock de órdenes pendientes");
                return ObtenerOrdenesCerradasMock();
            }

            try
            {
                using (var db = new SqlConnection(_connectionString))
                {
                    string sql = $@"
                        SELECT ID, U_Empresa, U_RampID, U_OrderDate, U_ShipDate, 
                               U_Action, U_Status, U_DocEntry, U_DocNum, 
                               U_ErrorMsg, U_RetryCount, CreatedAt, ProcessedAt
                        FROM {_linkServer}.{_dbBuffer}.dbo.ZRAMP_QUEUE 
                        WHERE U_Status = 'P' 
                        ORDER BY CreatedAt ASC";

                    return db.Query<ZRampQueue>(sql).ToList();
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error al consultar la cola ZRAMP_QUEUE.");
                return new List<ZRampQueue>();
            }
        }

        private class MockOrderList
        {
            public List<ShipmentOrder> Ordenes { get; set; }
        }

        private List<ZRampQueue> ObtenerOrdenesCerradasMock()
        {
            var mockList = new List<ZRampQueue>();
            var ordenes = ObtenerMultiplesOrdenesMock();

            if (ordenes == null || !ordenes.Any())
            {
                Logger.Error("[MOCK] ❌ No se pudieron cargar las órdenes mock. La lista de órdenes permanecerá VACÍA.");
                return mockList;
            }

            Logger.Info($"[MOCK] ✅ Se cargaron {ordenes.Count} órdenes mock exitosamente.");

            int idCounter = 1;
            foreach (var mockOrder in ordenes)
            {
                if (string.IsNullOrEmpty(mockOrder.OrderNumber))
                {
                    Logger.Warn($"[MOCK] ⚠️ Orden {idCounter} no contiene 'OrderNumber' - se omite.");
                    idCounter++;
                    continue;
                }

                if (mockOrder.Detalles == null || !mockOrder.Detalles.Any())
                {
                    Logger.Warn($"[MOCK] ⚠️ Orden {mockOrder.OrderNumber} no contiene 'Detalles' - se omite.");
                    idCounter++;
                    continue;
                }

                string empresa = !string.IsNullOrEmpty(mockOrder.FacilityName)
                    ? mockOrder.FacilityName.ToUpper()
                    : "PIN";

                DateTime orderDate = mockOrder.DocumentDate ?? DateTime.Now;
                DateTime shipDate = mockOrder.ActualShipDate ?? orderDate.AddDays(1);

                mockList.Add(new ZRampQueue
                {
                    ID = 999999 + idCounter,
                    U_Empresa = empresa,
                    U_RampID = mockOrder.OrderNumber,
                    U_OrderDate = orderDate,
                    U_ShipDate = shipDate,
                    U_Action = "CREATE",
                    U_Status = "P",
                    U_DocEntry = 0,
                    U_DocNum = 0,
                    U_ErrorMsg = "",
                    U_RetryCount = 0,
                    CreatedAt = DateTime.Now,
                    ProcessedAt = null
                });

                Logger.Info($"[MOCK] ✅ Registro mock {idCounter}: RampID={mockOrder.OrderNumber}, Empresa={empresa}");
                idCounter++;
            }

            return mockList;
        }

        private List<ShipmentOrder> ObtenerMultiplesOrdenesMock()
        {
            try
            {
                string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _mockFilePath);

                if (!File.Exists(fullPath))
                {
                    Logger.Error($"[MOCK] ❌ Archivo mock NO ENCONTRADO: {fullPath}");
                    return null;
                }

                string jsonContent = File.ReadAllText(fullPath);

                if (string.IsNullOrWhiteSpace(jsonContent))
                {
                    Logger.Error("[MOCK] ❌ El archivo JSON está VACÍO.");
                    return null;
                }

                // Intentar formato con wrapper "Ordenes"
                try
                {
                    var ordenesWrapper = JsonConvert.DeserializeObject<MockOrderList>(jsonContent);
                    if (ordenesWrapper?.Ordenes != null && ordenesWrapper.Ordenes.Any())
                    {
                        return ordenesWrapper.Ordenes;
                    }
                }
                catch (JsonException) { }

                // Intentar como array directo
                try
                {
                    var ordenesArray = JsonConvert.DeserializeObject<List<ShipmentOrder>>(jsonContent);
                    if (ordenesArray != null && ordenesArray.Any())
                    {
                        return ordenesArray;
                    }
                }
                catch (JsonException) { }

                // Fallback: orden única
                var ordenUnica = JsonConvert.DeserializeObject<ShipmentOrder>(jsonContent);
                if (ordenUnica != null)
                {
                    return new List<ShipmentOrder> { ordenUnica };
                }

                Logger.Error("[MOCK] ❌ No se pudo deserializar el JSON.");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[MOCK] ❌ Error al leer el archivo mock.");
                return null;
            }
        }

        /// <summary>
        /// Obtiene una orden completa (Mock o BD) y valida/obtiene los datos desde SAP
        /// </summary>
        public ShipmentOrder ObtenerOrdenCompleta(string rampId, string dbSap, string companyDB)
        {
            ShipmentOrder orden = null;

            if (DebeUsarMock())
            {
                Logger.Info($"[MOCK] Obteniendo orden mock para RAMP ID: {rampId}");
                orden = ObtenerOrdenCompletaMock(rampId);
            }
            else
            {
                orden = ObtenerOrdenCompletaDesdeBD(rampId, dbSap);
            }

            if (orden == null)
            {
                Logger.Error($"[ERROR] No se pudo obtener la orden para RAMP ID: {rampId}");
                return null;
            }

            // Obtener SapOrderDocEntry y SapCardCode desde SAP
            if (!GetSapOrderData(orden, companyDB))
            {
                Logger.Error($"[ERROR] No se pudo obtener los datos del pedido en SAP para la orden: {orden.OrderNumber}");
                return null;
            }

            return orden;
        }

        /// <summary>
        /// Obtiene el DocEntry y CardCode del pedido desde SAP usando el OrderNumber
        /// </summary>
        private bool GetSapOrderData(ShipmentOrder orden, string companyDB)
        {
            try
            {
                if (string.IsNullOrEmpty(orden.OrderNumber))
                {
                    Logger.Error("[VALIDACIÓN] OrderNumber vacío.");
                    return false;
                }

                string sql = $@"
                    SELECT TOP 1 DocEntry, CardCode, DocStatus, DocNum
                    FROM [{companyDB}].dbo.ORDR 
                    WHERE DocNum = @OrderNumber 
                    AND DocStatus = 'O'
                    AND Canceled = 'N'";

                using (var db = new SqlConnection(_connectionString))
                {
                    var resultado = db.QueryFirstOrDefault<dynamic>(sql, new { OrderNumber = orden.OrderNumber });

                    if (resultado == null)
                    {
                        Logger.Error($"[VALIDACIÓN] ❌ No se encontró pedido abierto con DocNum={orden.OrderNumber} en la empresa {companyDB}");
                        return false;
                    }

                    orden.SapOrderDocEntry = resultado.DocEntry;
                    orden.SapCardCode = resultado.CardCode;

                    Logger.Info($"[VALIDACIÓN] ✅ Pedido encontrado: DocNum={orden.OrderNumber} → DocEntry={orden.SapOrderDocEntry}, Cliente={orden.SapCardCode}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[VALIDACIÓN] Error al obtener datos del pedido {orden.OrderNumber}");
                return false;
            }
        }

        private ShipmentOrder ObtenerOrdenCompletaMock(string rampId)
        {
            try
            {
                var todasLasOrdenes = ObtenerMultiplesOrdenesMock();

                if (todasLasOrdenes == null || !todasLasOrdenes.Any())
                {
                    Logger.Error($"[MOCK] ❌ No se pudieron cargar órdenes para buscar RampID: {rampId}");
                    return null;
                }

                var mockOrder = todasLasOrdenes.FirstOrDefault(o => o.OrderNumber == rampId);

                if (mockOrder == null)
                {
                    Logger.Error($"[MOCK] ❌ No se encontró orden con RampID: {rampId}");
                    return null;
                }

                if (string.IsNullOrEmpty(mockOrder.OrderNumber))
                {
                    Logger.Error("[MOCK] ❌ El JSON no contiene 'OrderNumber'");
                    return null;
                }

                if (mockOrder.Detalles == null || !mockOrder.Detalles.Any())
                {
                    Logger.Error("[MOCK] ❌ El JSON no contiene 'Detalles'");
                    return null;
                }

                Logger.Info($"[MOCK] ✅ Orden cargada: {mockOrder.OrderNumber}, Líneas={mockOrder.Detalles.Count}");
                return mockOrder;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[MOCK] ❌ Error obteniendo orden mock para RampID: {rampId}");
                return null;
            }
        }

        private ShipmentOrder ObtenerOrdenCompletaDesdeBD(string rampId, string dbSap)
        {
            try
            {
                using (var db = new SqlConnection(_connectionString))
                {
                    string sql = $@"
                        SELECT 
                            h.OrderNumber, 
                            h.DocumentDate,
                            d.WarehouseSku,
                            d.QtyShipped,
                            COALESCE(sapD.LineNum, 0) AS SapOrderLineNum
                        FROM {_linkServer}.{_dbReal}.dbo.ShipmentOrder h
                        INNER JOIN {_linkServer}.{_dbReal}.dbo.ShipmentOrderDetail d 
                            ON h.OrderNumber = d.OrderNumber COLLATE DATABASE_DEFAULT
                        LEFT JOIN [{dbSap}].dbo.ORDR sapH 
                            ON sapH.NumAtCard = h.OrderNumber COLLATE DATABASE_DEFAULT 
                            AND sapH.DocStatus = 'O'
                        LEFT JOIN [{dbSap}].dbo.RDR1 sapD 
                            ON sapD.DocEntry = sapH.DocEntry 
                            AND sapD.ItemCode = d.WarehouseSku COLLATE DATABASE_DEFAULT
                        WHERE h.OrderNumber = @rampId
                        ORDER BY SapOrderLineNum ASC";

                    var queryResult = db.Query<dynamic>(sql, new { rampId });

                    if (queryResult == null || !queryResult.Any())
                    {
                        Logger.Warn($"No se encontraron datos para RAMP ID: {rampId}");
                        return null;
                    }

                    ShipmentOrder encabezado = null;
                    var detalles = new List<ShipmentOrderDetail>();

                    foreach (var row in queryResult)
                    {
                        if (encabezado == null)
                        {
                            encabezado = new ShipmentOrder
                            {
                                OrderNumber = row.OrderNumber,
                                DocumentDate = row.DocumentDate,
                                Detalles = new List<ShipmentOrderDetail>()
                            };
                        }

                        if (!string.IsNullOrEmpty((string)row.WarehouseSku))
                        {
                            detalles.Add(new ShipmentOrderDetail
                            {
                                WarehouseSku = row.WarehouseSku,
                                QtyShipped = row.QtyShipped ?? 0,
                                SapOrderLineNum = row.SapOrderLineNum ?? 0
                            });
                        }
                    }

                    if (encabezado != null)
                    {
                        encabezado.Detalles = detalles;
                        Logger.Info($"Orden cargada desde BD: OrderNumber={encabezado.OrderNumber}, Líneas={encabezado.Detalles.Count}");
                    }

                    return encabezado;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error al obtener detalle de la orden RAMP: {rampId}");
                return null;
            }
        }

        public void ActualizarResultado(long id, string status, int docEntry, int docNum, string errorMsg = "")
        {
            // ⭐ CAMBIO: Simular actualización solo si ModoProceso = TEST
            if (DebeSimularActualizacion())
            {
                Logger.Info($"[SIMULACIÓN] ID {id} -> Status={status}, DocEntry={docEntry}, DocNum={docNum}");
                if (!string.IsNullOrEmpty(errorMsg))
                    Logger.Info($"[SIMULACIÓN] ErrorMsg: {errorMsg}");
                Logger.Info("[SIMULACIÓN] No se actualizó la base de datos (modo simulación).");
                return;
            }

            // ⭐ ELIMINADO: Bloque que prohibía MOCK + PRD

            // Modo PRD - actualizar BD
            try
            {
                using (var db = new SqlConnection(_connectionString))
                {
                    if (!string.IsNullOrEmpty(errorMsg) && errorMsg.Length > 250)
                        errorMsg = errorMsg.Substring(0, 247) + "...";

                    string sql = $@"
                        UPDATE {_linkServer}.{_dbBuffer}.dbo.ZRAMP_QUEUE 
                        SET U_Status = @status, 
                            U_DocEntry = @docEntry,
                            U_DocNum = @docNum,
                            U_ErrorMsg = @errorMsg, 
                            ProcessedAt = GETDATE() 
                        WHERE ID = @id";

                    db.Execute(sql, new { id, status, docEntry, docNum, errorMsg });
                    Logger.Info($"Actualizado registro ID {id} con Status={status}");
                }
            }
            catch (Exception ex)
            {
                Logger.Fatal(ex, $"Error actualizando ID {id}");
            }
        }
    }
}