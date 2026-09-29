namespace ClothingAPI.Models
{
    public class PurchaseOrderItem
    {
        public int PurchaseOrderItemId { get; set; }

        public int PurchaseOrderId { get; set; }

        public int VariantId { get; set; }

        // Snapshot tại thời điểm nhập kho.
        public string ProductName { get; set; } = string.Empty;

        public string SKU { get; set; } = string.Empty;

        public string ColorName { get; set; } = string.Empty;

        public string SizeName { get; set; } = string.Empty;

        public decimal UnitCost { get; set; }

        public int Quantity { get; set; }

        public decimal LineTotal { get; set; }

        public PurchaseOrder PurchaseOrder { get; set; } = null!;

        public ProductVariant Variant { get; set; } = null!;
    }
}
