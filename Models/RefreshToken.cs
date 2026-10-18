namespace ClothingAPI.Models
{
    /// Refresh token đã cấp. Chỉ lưu SHA-256 hash, KHÔNG lưu bản gốc (bản gốc chỉ nằm trong cookie HttpOnly của trình duyệt).
    public class RefreshToken
    {
        public int RefreshTokenId { get; set; }

        public int UserId { get; set; }

        public string TokenHash { get; set; } = string.Empty;

        // Một "family" = một phiên đăng nhập trên một thiết bị; mọi token sinh ra khi xoay vòng đều cùng FamilyId.
        public Guid FamilyId { get; set; }

        public DateTime ExpiresAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public string? CreatedByIp { get; set; }

        public string? UserAgent { get; set; }

        public DateTime? RevokedAt { get; set; }

        // Lý do thu hồi (Rotated, Logout, ReuseDetected, ...) để dễ theo dõi khi demo/điều tra.
        public string? RevokedReason { get; set; }

        public int? ReplacedByTokenId { get; set; }

        public User User { get; set; } = null!;

        public RefreshToken? ReplacedByToken { get; set; }
    }
}
