using ClothingAPI.Enums;
using System.ComponentModel.DataAnnotations;

namespace ClothingAPI.DTOs.Products
{
    public class UpsertProductDto
    {
        [Range(1, int.MaxValue, ErrorMessage = "Danh mục không hợp lệ.")]
        public int CategoryId { get; set; }

        public int? BrandId { get; set; }

        [Required(ErrorMessage = "Tên sản phẩm là bắt buộc.")]
        [StringLength(250)]
        public string ProductName { get; set; } = string.Empty;

        [StringLength(500)]
        public string? ShortDescription { get; set; }

        [Required(ErrorMessage = "Mô tả chi tiết là bắt buộc.")]
        public string Description { get; set; } = string.Empty;

        [StringLength(150)]
        public string? Material { get; set; }

        [Required(ErrorMessage = "Giới tính là bắt buộc.")]
        public Gender Gender { get; set; }

        [Range(typeof(decimal), "0.01", "9999999999999999")]
        public decimal BasePrice { get; set; }

        [Range(typeof(decimal), "0.01", "9999999999999999")]
        public decimal? SalePrice { get; set; }

        public bool IsFeatured { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
