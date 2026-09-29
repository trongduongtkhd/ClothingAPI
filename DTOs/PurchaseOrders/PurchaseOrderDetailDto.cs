namespace ClothingAPI.DTOs.PurchaseOrders
{
    public class PurchaseOrderDetailDto
    {
        public int PurchaseOrderId { get; set; }

        public string PurchaseOrderCode { get; set; } = string.Empty;

        public int SupplierId { get; set; }

        public string SupplierName { get; set; } = string.Empty;

        public string? SupplierPhone { get; set; }

        public string CreatedByUserName { get; set; } = string.Empty;

        public string? Note { get; set; }

        public decimal TotalAmount { get; set; }

        public string Status { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public DateTime? CancelledAt { get; set; }

        public string? CancelReason { get; set; }

        public List<PurchaseOrderItemDto> Items { get; set; } = [];
    }

    public class PurchaseOrderItemDto
    {
        public int PurchaseOrderItemId { get; set; }

        public int VariantId { get; set; }

        public string ProductName { get; set; } = string.Empty;

        public string SKU { get; set; } = string.Empty;

        public string ColorName { get; set; } = string.Empty;

        public string SizeName { get; set; } = string.Empty;

        public decimal UnitCost { get; set; }

        public int Quantity { get; set; }

        public decimal LineTotal { get; set; }
    }
}
