using System.ComponentModel.DataAnnotations;

namespace ClothingAPI.DTOs.Brands
{
    public class UpsertBrandDto
    {
        [Required(ErrorMessage = "Tên thương hiệu là bắt buộc.")]
        [StringLength(150, ErrorMessage = "Tên thương hiệu không được vượt quá 150 ký tự.")]
        public string BrandName { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        [StringLength(500)]
        public string? LogoUrl { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
