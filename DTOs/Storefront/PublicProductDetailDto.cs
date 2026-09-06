using ClothingAPI.DTOs.Products;
using ClothingAPI.Enums;

namespace ClothingAPI.DTOs.Storefront
{
    public class PublicProductDetailDto
    {
        public int ProductId { get; set; }

        public string ProductName { get; set; } = string.Empty;

        public string Slug { get; set; } = string.Empty;

        public string CategoryName { get; set; } = string.Empty;

        public string? BrandName { get; set; }

        public string? ShortDescription { get; set; }

        public string Description { get; set; } = string.Empty;

        public string? Material { get; set; }
        public Gender Gender { get; set; }

        public decimal BasePrice { get; set; }

        public decimal? SalePrice { get; set; }

        public List<ProductImageDto> Images { get; set; } = [];

        public List<ProductVariantDto> Variants { get; set; } = [];

    }
}
