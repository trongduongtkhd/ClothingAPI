using System.ComponentModel.DataAnnotations;

namespace ClothingAPI.DTOs.Auth
{
    public class ResetPasswordDto
    {
        [Required(ErrorMessage = "Token là bắt buộc.")]
        public string Token { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mật khẩu mới là bắt buộc.")]
        [MinLength(6, ErrorMessage = "Mật khẩu mới phải có ít nhất 6 ký tự.")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Xác nhận mật khẩu là bắt buộc.")]
        [Compare(nameof(NewPassword), ErrorMessage = "Xác nhận mật khẩu không khớp.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
