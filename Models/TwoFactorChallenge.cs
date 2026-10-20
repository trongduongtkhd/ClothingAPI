namespace ClothingAPI.Models
{
    /// Phiên chờ nhập mã 2FA sau khi mật khẩu đúng. Token gốc là chuỗi ngẫu nhiên đục (KHÔNG phải JWT)
    /// nên không thể dùng thay access token; DB chỉ lưu SHA-256 hash.
    public class TwoFactorChallenge
    {
        public int TwoFactorChallengeId { get; set; }

        public int UserId { get; set; }

        public string TokenHash { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime ExpiresAt { get; set; }

        public int FailedAttempts { get; set; }

        // Đã dùng thành công (challenge chỉ dùng một lần).
        public DateTime? ConsumedAt { get; set; }

        public string? CreatedByIp { get; set; }

        public User User { get; set; } = null!;
    }
}
