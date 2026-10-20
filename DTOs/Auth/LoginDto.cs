using System.ComponentModel.DataAnnotations;

namespace ClothingAPI.DTOs.Auth
{
    public class LoginDto
    {
        [Required(ErrorMessage = "Email là bắt buộc.")]
        [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mật khẩu là bắt buộc.")]
        public string Password { get; set; } = string.Empty;

        // Token Cloudflare Turnstile (dùng một lần, hết hạn sau ~5 phút).
        [Required(ErrorMessage = "Vui lòng hoàn tất xác minh CAPTCHA.")]
        public string CaptchaToken { get; set; } = string.Empty;
    }
}
