namespace ClothingAPI.Models
{
    public class Category
    {
        public int CategoryId { get; set; }

        public int? ParentCategoryId { get; set; }

        public string CategoryName { get; set; } = string.Empty;

        public string Slug { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? ImageUrl { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Category? ParentCategory { get; set; }

        public ICollection<Category> ChildCategories { get; set; } = new List<Category>();
        public ICollection<Product> Products { get; set; } = new List<Product>();


    }
}
