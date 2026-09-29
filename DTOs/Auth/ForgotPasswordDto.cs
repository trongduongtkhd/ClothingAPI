using System.ComponentModel.DataAnnotations;

namespace ClothingAPI.DTOs.Auth
{
    public class ForgotPasswordDto
    {
        [Required(ErrorMessage = "Email là bắt buộc.")]
        [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
        public string Email { get; set; } = string.Empty;
    }
}
