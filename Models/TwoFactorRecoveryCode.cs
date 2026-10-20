namespace ClothingAPI.Models
{
    /// Mã khôi phục 2FA dùng một lần. Chỉ lưu HMAC-SHA256 (khóa = pepper), KHÔNG lưu mã gốc.
    public class TwoFactorRecoveryCode
    {
        public int TwoFactorRecoveryCodeId { get; set; }

        public int UserId { get; set; }

        public string CodeHash { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UsedAt { get; set; }

        public User User { get; set; } = null!;
    }
}
