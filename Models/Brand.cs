namespace ClothingAPI.Models
{
    public class Brand
    {
        public int BrandId { get; set; }

        public string BrandName { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? LogoUrl { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public ICollection<Product> Products { get; set; } = new List<Product>();

    }
}
