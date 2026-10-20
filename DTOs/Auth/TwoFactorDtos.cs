using System.ComponentModel.DataAnnotations;

namespace ClothingAPI.DTOs.Auth
{
    /// Phản hồi bước 1 khi tài khoản bật 2FA: KHÔNG có access token, chỉ có challenge đục dùng cho bước 2.
    public class TwoFactorChallengeDto
    {
        public bool RequiresTwoFactor { get; set; } = true;

        public string TwoFactorToken { get; set; } = string.Empty;

        public DateTime ExpiresAt { get; set; }

        // Frontend đếm ngược theo giá trị này (không phụ thuộc lệch đồng hồ máy khách).
        public int ExpiresInSeconds { get; set; }
    }

    public class TwoFactorVerifyDto
    {
        [Required(ErrorMessage = "Thiếu phiên xác thực.")]
        [StringLength(128)]
        public string TwoFactorToken { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mã xác thực là bắt buộc.")]
        [StringLength(10)]
        public string Code { get; set; } = string.Empty;
    }

    public class TwoFactorRecoveryDto
    {
        [Required(ErrorMessage = "Thiếu phiên xác thực.")]
        [StringLength(128)]
        public string TwoFactorToken { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mã khôi phục là bắt buộc.")]
        [StringLength(30)]
        public string RecoveryCode { get; set; } = string.Empty;
    }

    /// Kết quả đăng nhập bước 1 (nội bộ service): đúng một trong hai trường khác null.
    public record LoginResult(AuthResponseDto? Auth, TwoFactorChallengeDto? Challenge)
    {
        public bool RequiresTwoFactor => Challenge is not null;
    }

    public enum TwoFactorLoginStatus
    {
        Success,
        InvalidCode,
        Replayed,
        ChallengeExpired,
        LockedOut
    }

    /// Kết quả đăng nhập bước 2. Không throw cho lỗi dự kiến (sai mã...) để ExceptionMiddleware không ghi LogError.
    public record TwoFactorLoginResult(
        TwoFactorLoginStatus Status,
        int UserId = 0,
        int RemainingLockoutMinutes = 0,
        int? RemainingRecoveryCodes = null)
    {
        public bool Succeeded => Status == TwoFactorLoginStatus.Success;
    }
}
