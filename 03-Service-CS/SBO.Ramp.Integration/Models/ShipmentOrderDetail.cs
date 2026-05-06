using System;

namespace SBO.Ramp.Integration.Models
{
    public class ShipmentOrderDetail
    {
        // --- Campos Originales de RAMP ---
        public string FacilityName { get; set; }
        public string CustomerName { get; set; }
        public string OrderNumber { get; set; }
        public string OrderLineNumber { get; set; }
        public string WarehouseSku { get; set; }
        public string Description { get; set; }
        public int QtyOrdered { get; set; }
        public int QtyAllocated { get; set; }
        public int QtyShipped { get; set; }
        public int QtyOpen { get; set; }

        // --- Campos de Vinculación con SAP (Cruciales) ---

        // El ItemCode real en SAP
        public string SapItemCode { get; set; }

        // El DocEntry del Pedido (Cabecera) que viene del JOIN en el Repo
        public int SapOrderDocEntry { get; set; }

        // El LineNum del Pedido (Línea) que viene del JOIN en el Repo
        // Nota: Cambié SapOrderLineBase a SapOrderLineNum para que coincida con el Service
        public int SapOrderLineNum { get; set; }
    }
}