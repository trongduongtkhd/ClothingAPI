namespace ClothingAPI.DTOs.Storefront
{
    public class PublicProductListDto
    {
        public int ProductId { get; set; }

        public string ProductName { get; set; } = string.Empty;

        public string Slug { get; set; } = string.Empty;

        public string CategoryName { get; set; } = string.Empty;

        public string? BrandName { get; set; }

        public decimal BasePrice { get; set; }

        public decimal? SalePrice { get; set; }

        public string? ThumbnailUrl { get; set; }

        public bool IsFeatured { get; set; }
    }
}
