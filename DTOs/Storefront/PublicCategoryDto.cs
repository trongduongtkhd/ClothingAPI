namespace ClothingAPI.DTOs.Storefront
{
    public class PublicCategoryDto
    {
        public int CategoryId { get; set; }

        public string CategoryName { get; set; } = string.Empty;

        public string Slug { get; set; } = string.Empty;

        public string? Description { get; set; }
        public string? ImageUrl { get; set; }

        public List<PublicCategoryDto> Children { get; set; } = [];
    }
}
