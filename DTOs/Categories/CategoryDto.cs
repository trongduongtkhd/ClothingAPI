namespace ClothingAPI.DTOs.Categories
{
    public class CategoryDto
    {
        public int CategoryId { get; set; }

        public int? ParentCategoryId { get; set; }

        public string? ParentCategoryName { get; set; }

        public string CategoryName { get; set; } = string.Empty;

        public string Slug { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? ImageUrl { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
