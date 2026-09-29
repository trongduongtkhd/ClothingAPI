namespace ClothingAPI.DTOs.Suppliers
{
    public class SupplierDto
    {
        public int SupplierId { get; set; }

        public string SupplierName { get; set; } = string.Empty;

        public string? ContactName { get; set; }

        public string? Phone { get; set; }

        public string? Email { get; set; }

        public string? Address { get; set; }

        public string? TaxCode { get; set; }

        public string? Description { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
