using ClothingAPI.Enums;

namespace ClothingAPI.DTOs.Products
{
    public class ProductDetailDto
    {
        public int ProductId { get; set; }

        public int CategoryId { get; set; }

        public string CategoryName { get; set; } = string.Empty;

        public int? BrandId { get; set; }

        public string? BrandName { get; set; }

        public string ProductName { get; set; } = string.Empty;

        public string Slug { get; set; } = string.Empty;

        public string? ShortDescription { get; set; }

        public string Description { get; set; } = string.Empty;

        public string? Material { get; set; }

        public Gender Gender { get; set; }

        public decimal BasePrice { get; set; }

        public decimal? SalePrice { get; set; }

        public bool IsFeatured { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public List<ProductVariantDto> Variants { get; set; } = [];

        public List<ProductImageDto> Images { get; set; } = [];
    }
}
