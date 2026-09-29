namespace ClothingAPI.Models
{
    public class Supplier
    {
        public int SupplierId { get; set; }

        public string SupplierName { get; set; } = string.Empty;

        public string? ContactName { get; set; }

        public string? Phone { get; set; }

        public string? Email { get; set; }

        public string? Address { get; set; }

        public string? TaxCode { get; set; }

        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<PurchaseOrder> PurchaseOrders { get; set; } = new List<PurchaseOrder>();
    }
}
