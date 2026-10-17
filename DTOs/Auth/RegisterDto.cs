using System.ComponentModel.DataAnnotations;
using ClothingAPI.Helpers;

namespace ClothingAPI.DTOs.Auth
{
    public class RegisterDto
    {
        [Required(ErrorMessage = "Họ tên là bắt buộc.")]
        [StringLength(150, ErrorMessage = "Họ tên không được vượt quá 150 ký tự.")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email là bắt buộc.")]
        [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
        // [EmailAddress] vẫn chấp nhận "abc@gmail" (thiếu tên miền), nên bắt buộc thêm phần ".xx" ở cuối.
        [RegularExpression(@"^\s*[^\s@]+@[^\s@]+\.[A-Za-z]{2,}\s*$", ErrorMessage = "Email không đúng định dạng.")]
        [StringLength(255)]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mật khẩu là bắt buộc.")]
        [StrongPassword]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Xác nhận mật khẩu là bắt buộc.")]
        [Compare(nameof(Password), ErrorMessage = "Xác nhận mật khẩu không khớp.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [StringLength(20)]
        public string? PhoneNumber { get; set; }
    }
}
