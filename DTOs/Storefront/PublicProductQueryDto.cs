using ClothingAPI.Enums;

namespace ClothingAPI.DTOs.Storefront
{
    public class PublicProductQueryDto
    {
        public string? Keyword { get; set; }

        public int? CategoryId { get; set; }

        public int? BrandId { get; set; }

        public Gender? Gender { get; set; }

        public decimal? MinPrice { get; set; }

        public decimal? MaxPrice { get; set; }

        // newest | priceAsc | priceDesc | bestSelling
        public string? Sort { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 12;
    }
}
