using System.ComponentModel.DataAnnotations;

namespace ClothingAPI.DTOs.Uploads
{
    public class UploadImageRequestDto
    {
        [Required(ErrorMessage = "Vui lòng chọn file ảnh.")]
        public IFormFile File { get; set; } = default!;
    }
}
