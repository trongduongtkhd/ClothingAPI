using System.ComponentModel.DataAnnotations;

namespace ClothingAPI.DTOs.Users
{
    public class UpdateProfileDto
    {
        [Required(ErrorMessage = "Họ tên là bắt buộc.")]
        [StringLength(150)]
        public string FullName { get; set; } = string.Empty;

        [StringLength(20)]
        public string? PhoneNumber { get; set; }

        [StringLength(500)]
        public string? AvatarUrl { get; set; }
    }
}
