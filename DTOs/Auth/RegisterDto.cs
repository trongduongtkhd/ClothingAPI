using System.ComponentModel.DataAnnotations;
using ClothingAPI.Helpers;

namespace ClothingAPI.DTOs.Auth
{
    public class RegisterDto
    {
        private string _fullName = string.Empty;
        private string _email = string.Empty;
        private string? _phoneNumber;

        [Required(ErrorMessage = "Họ tên là bắt buộc.")]
        [StringLength(150, ErrorMessage = "Họ tên không được vượt quá 150 ký tự.")]
        [PersonName]
        public string FullName { get => _fullName; set => _fullName = UserInputRules.NormalizeFullName(value); }

        [Required(ErrorMessage = "Email là bắt buộc.")]
        [AccountEmail]
        public string Email { get => _email; set => _email = UserInputRules.NormalizeEmail(value); }

        [Required(ErrorMessage = "Mật khẩu là bắt buộc.")]
        [StrongPassword]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Xác nhận mật khẩu là bắt buộc.")]
        [Compare(nameof(Password), ErrorMessage = "Xác nhận mật khẩu không khớp.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [VietnameseMobilePhone]
        public string? PhoneNumber { get => _phoneNumber; set => _phoneNumber = UserInputRules.NormalizePhone(value); }

        // Token Cloudflare Turnstile (dùng một lần, hết hạn sau ~5 phút).
        [Required(ErrorMessage = "Vui lòng hoàn tất xác minh CAPTCHA.")]
        public string CaptchaToken { get; set; } = string.Empty;
    }
}
