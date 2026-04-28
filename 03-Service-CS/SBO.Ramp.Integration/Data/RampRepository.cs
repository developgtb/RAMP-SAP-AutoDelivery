using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Linq;
using Dapper;
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
        private readonly string _modoProceso; // Almacenamos el modo para evitar lecturas repetitivas

        public RampRepository()
        {
            _connectionString = ConfigurationManager.ConnectionStrings["SQLSBO"]?.ConnectionString;
            _linkServer = ConfigurationManager.AppSettings["LinkedServerRamp"];
            _dbBuffer = ConfigurationManager.AppSettings["DbBufferRamp"];
            _dbReal = ConfigurationManager.AppSettings["DbRealRamp"];

            // Leemos el modo de proceso al instanciar
            _modoProceso = ConfigurationManager.AppSettings["ModoProceso"]?.ToUpper() ?? "TEST";

            ValidarConfiguracion();
        }

        private void ValidarConfiguracion()
        {
            if (string.IsNullOrEmpty(_connectionString)) throw new Exception("Falta 'SQLSBO' en connectionStrings.");
            if (string.IsNullOrEmpty(_linkServer)) throw new Exception("Falta 'LinkedServerRamp' en appSettings.");
            if (string.IsNullOrEmpty(_dbBuffer)) throw new Exception("Falta 'DbBufferRamp' en appSettings.");
        }

        /// <summary>
        /// Obtiene los registros con estatus Pendiente ('P').
        /// Siempre lee de la BD, incluso en modo TEST, para poder simular la carga.
        /// </summary>
        public List<ZRampQueue> ObtenerOrdenesCerradas()
        {
            try
            {
                using (var db = new SqlConnection(_connectionString))
                {
                    string sql = $@"
                        SELECT 
                            ID, U_Empresa, U_RampID, U_OrderDate, U_ShipDate, 
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
                Logger.Error(ex, "Error al consultar la cola ZRAMP_QUEUE en el servidor RAMP.");
                return new List<ZRampQueue>();
            }
        }

        /// <summary>
        /// Cruza datos de RAMP con la base de datos de SAP específica.
        /// </summary>
        public ShipmentOrder ObtenerOrdenCompleta(string rampId, string dbSap)
        {
            try
            {
                using (var db = new SqlConnection(_connectionString))
                {
                    string sql = $@"
                        SELECT 
                            h.OrderNumber, h.SapCardCode, h.DocumentDate,
                            d.SapItemCode, d.QtyShipped,
                            sapH.DocEntry AS SapOrderDocEntry,
                            sapD.LineNum AS SapOrderLineNum
                        FROM {_linkServer}.{_dbReal}.dbo.ShipmentOrder h
                        INNER JOIN {_linkServer}.{_dbReal}.dbo.ShipmentOrderDetail d ON h.OrderNumber = d.OrderNumber
                        LEFT JOIN [{dbSap}].dbo.ORDR sapH ON sapH.NumAtCard = h.OrderNumber AND sapH.DocStatus = 'O'
                        LEFT JOIN [{dbSap}].dbo.RDR1 sapD ON sapD.DocEntry = sapH.DocEntry AND sapD.ItemCode = d.SapItemCode
                        WHERE h.OrderNumber = @rampId";

                    var lookup = new Dictionary<string, ShipmentOrder>();

                    db.Query<ShipmentOrder, ShipmentOrderDetail, ShipmentOrder>(sql,
                    (header, detail) => {
                        if (!lookup.TryGetValue(header.OrderNumber, out var currentHeader))
                        {
                            currentHeader = header;
                            currentHeader.Detalles = new List<ShipmentOrderDetail>();
                            lookup.Add(header.OrderNumber, currentHeader);
                        }
                        currentHeader.Detalles.Add(detail);
                        return currentHeader;
                    }, new { rampId }, splitOn: "SapItemCode");

                    return lookup.Values.FirstOrDefault();
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error al obtener detalle de la orden RAMP: {rampId}");
                return null;
            }
        }

        /// <summary>
        /// Actualiza el resultado. Si está en modo TEST, aborta la escritura físicamente.
        /// </summary>
        public void ActualizarResultado(long id, string status, int docEntry, int docNum, string errorMsg = "")
        {
            // --- PROTECCIÓN MODO TEST ---
            if (_modoProceso == "TEST")
            {
                Logger.Info($"[SIMULACIÓN] El registro ID {id} NO fue actualizado en BD (Modo TEST activo). " +
                            $"Datos proyectados: Status={status}, DocEntry={docEntry}, Error={errorMsg}");
                return;
            }

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
                    Logger.Debug($"[BD] Registro {id} actualizado correctamente a status {status}.");
                }
            }
            catch (Exception ex)
            {
                Logger.Fatal(ex, $"No se pudo actualizar el resultado real para el ID {id} en la tabla de cola.");
            }
        }
    }
}