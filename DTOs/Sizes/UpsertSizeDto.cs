using System.ComponentModel.DataAnnotations;

namespace ClothingAPI.DTOs.Sizes
{
    public class UpsertSizeDto
    {
        [Required(ErrorMessage = "Tên size là bắt buộc.")]
        [StringLength(20, ErrorMessage = "Tên size không được vượt quá 20 ký tự.")]
        public string SizeName { get; set; } = string.Empty;

        [Range(0, 1000, ErrorMessage = "Thứ tự hiển thị phải từ 0 đến 1000.")]
        public int DisplayOrder { get; set; }
    }
}
