namespace ClothingAPI.DTOs.PurchaseOrders
{
    public class PurchaseOrderListDto
    {
        public int PurchaseOrderId { get; set; }

        public string PurchaseOrderCode { get; set; } = string.Empty;

        public int SupplierId { get; set; }

        public string SupplierName { get; set; } = string.Empty;

        public string CreatedByUserName { get; set; } = string.Empty;

        public int TotalItemCount { get; set; }

        public int TotalQuantity { get; set; }

        public decimal TotalAmount { get; set; }

        public string Status { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
    }
}
