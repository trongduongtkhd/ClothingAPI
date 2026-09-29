namespace ClothingAPI.Models
{
    public class PurchaseOrder
    {
        public int PurchaseOrderId { get; set; }

        public string PurchaseOrderCode { get; set; } = string.Empty;

        public int SupplierId { get; set; }

        public int CreatedByUserId { get; set; }

        public string? Note { get; set; }

        public decimal TotalAmount { get; set; }

        // Completed: đã nhập kho (tồn kho đã được cộng). Cancelled: đã hủy (tồn kho đã được trừ lại).
        public string Status { get; set; } = PurchaseOrderStatuses.Completed;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public DateTime? CancelledAt { get; set; }

        public string? CancelReason { get; set; }

        public Supplier Supplier { get; set; } = null!;

        public User CreatedByUser { get; set; } = null!;

        public ICollection<PurchaseOrderItem> Items { get; set; } = new List<PurchaseOrderItem>();
    }

    public static class PurchaseOrderStatuses
    {
        public const string Completed = "Completed";
        public const string Cancelled = "Cancelled";
    }
}
