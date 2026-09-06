using System.ComponentModel.DataAnnotations;

namespace ClothingAPI.DTOs.Colors
{
    public class UpsertColorDto
    {
        [Required(ErrorMessage = "Tên màu là bắt buộc.")]
        [StringLength(50, ErrorMessage = "Tên màu không được vượt quá 50 ký tự.")]
        public string ColorName { get; set; } = string.Empty;

        [StringLength(20)]
        public string? ColorCode { get; set; }
    }
}
