using ClothingAPI.Enums;

namespace ClothingAPI.Models
{
    public class Product
    {
        public int ProductId { get; set; }
        public int CategoryId { get; set; }
        public int? BrandId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string? ShortDescription { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? Material { get; set; }
        public Gender Gender { get; set; }
        public decimal BasePrice { get; set; }
        public decimal? SalePrice { get; set; }
        public bool IsFeatured { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public Category Category { get; set; } = null!;
        public Brand? Brand { get; set; }
        public ICollection<ProductVariant> ProductVariants { get; set; } = new List<ProductVariant>();
        public ICollection<ProductImage> ProductImages { get; set; } = new List<ProductImage>();
        public ICollection<Review> Reviews { get; set; } = new List<Review>();


    }
}
