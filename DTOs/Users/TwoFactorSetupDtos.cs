using System.ComponentModel.DataAnnotations;

namespace ClothingAPI.DTOs.Users
{
    /// Chỉ trả về khi 2FA CHƯA bật. Sau khi bật, không endpoint nào trả lại secret.
    public class TwoFactorSetupDto
    {
        // Base32, chia nhóm 4 ký tự để người dùng nhập tay vào ứng dụng xác thực.
        public string Secret { get; set; } = string.Empty;

        public string OtpauthUri { get; set; } = string.Empty;

        // Ảnh QR dạng data:image/png;base64,... (sinh ở backend bằng QRCoder).
        public string QrCodeDataUri { get; set; } = string.Empty;
    }

    public class EnableTwoFactorDto
    {
        [Required(ErrorMessage = "Mã xác thực là bắt buộc.")]
        [StringLength(10)]
        public string Code { get; set; } = string.Empty;
    }

    public class EnableTwoFactorResultDto
    {
        // Mã khôi phục dạng XXXX-XXXX-XXXX, chỉ hiển thị MỘT lần.
        public List<string> RecoveryCodes { get; set; } = [];
    }

    public class DisableTwoFactorDto
    {
        [Required(ErrorMessage = "Mật khẩu là bắt buộc.")]
        public string Password { get; set; } = string.Empty;

        // Nhập một trong hai: mã từ ứng dụng xác thực hoặc mã khôi phục.
        [StringLength(10)]
        public string? Code { get; set; }

        [StringLength(30)]
        public string? RecoveryCode { get; set; }
    }
}
