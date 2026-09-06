using System.ComponentModel.DataAnnotations;

namespace ClothingAPI.DTOs.Products
{
    public class UpsertProductImageDto
    {
        [Required(ErrorMessage = "Đường dẫn ảnh là bắt buộc.")]
        [StringLength(500)]
        public string ImageUrl { get; set; } = string.Empty;

        [Range(0, 1000)]
        public int DisplayOrder { get; set; }

        public bool IsThumbnail { get; set; }
    }
}
