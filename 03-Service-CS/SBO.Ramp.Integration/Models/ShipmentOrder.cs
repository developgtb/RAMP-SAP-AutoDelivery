using System;
using System.Collections.Generic;

namespace SBO.Ramp.Integration.Models
{
    public class ShipmentOrder
    {
        // --- Cabecera de RAMP ---
        public string FacilityName { get; set; }
        public string CustomerName { get; set; }
        public string OrderNumber { get; set; }
        public DateTime? DocumentDate { get; set; }
        public int Status { get; set; }
        public int DocumentStatus { get; set; }
        public DateTime? ActualShipDate { get; set; }
        public int QtyOrdered { get; set; }
        public int QtyShipped { get; set; }
        public int QtyOpen { get; set; }

        // --- Datos vinculados de SAP ---
        public string SapCardCode { get; set; }
        public int SapOrderDocEntry { get; set; } // DocEntry de la ORDR

        // Propiedad de navegación
        public List<ShipmentOrderDetail> Detalles { get; set; } = new List<ShipmentOrderDetail>();
    }
}