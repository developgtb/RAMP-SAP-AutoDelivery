using System;

namespace SBO.Ramp.Integration.Models
{
    public class ZRampQueue
    {
        // El [bigint] de SQL mapea a 'long' en C#
        public long ID { get; set; }
        public string U_Empresa { get; set; }
        public string U_RampID { get; set; }
        public DateTime U_OrderDate { get; set; }
        public DateTime? U_ShipDate { get; set; }
        public string U_Action { get; set; }
        // 'P', 'C', 'E', etc.
        public string U_Status { get; set; }
        public int U_DocEntry { get; set; }
        public int U_DocNum { get; set; }
        public string U_ErrorMsg { get; set; }
        public int U_RetryCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }

        // Propiedad de conveniencia para usar en los logs
        public override string ToString()
        {
            return $"ID: {ID} | Orden: {U_RampID} | BD: {U_Empresa} | Status: {U_Status}";
        }
    }
}