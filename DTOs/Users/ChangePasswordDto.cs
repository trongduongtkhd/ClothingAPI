using System.ComponentModel.DataAnnotations;

namespace ClothingAPI.DTOs.Users
{
    public class ChangePasswordDto
    {
        [Required(ErrorMessage = "Mật khẩu hiện tại là bắt buộc.")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mật khẩu mới là bắt buộc.")]
        [MinLength(6, ErrorMessage = "Mật khẩu mới phải có ít nhất 6 ký tự.")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Xác nhận mật khẩu mới là bắt buộc.")]
        [Compare(nameof(NewPassword), ErrorMessage = "Xác nhận mật khẩu không khớp.")]
        public string ConfirmNewPassword { get; set; } = string.Empty;
    }
}
