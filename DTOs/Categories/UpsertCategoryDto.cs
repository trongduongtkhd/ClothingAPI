using System.ComponentModel.DataAnnotations;

namespace ClothingAPI.DTOs.Categories
{
    public class UpsertCategoryDto
    {
        public int? ParentCategoryId { get; set; }

        [Required(ErrorMessage = "Tên danh mục là bắt buộc.")]
        [StringLength(150, ErrorMessage = "Tên danh mục không được vượt quá 150 ký tự.")]
        public string CategoryName { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        [StringLength(500)]
        public string? ImageUrl { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
