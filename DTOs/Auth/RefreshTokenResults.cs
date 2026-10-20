namespace ClothingAPI.DTOs.Auth
{
    /// Refresh token vừa cấp: RawToken chỉ dùng để set cookie, không bao giờ trả trong body JSON.
    public record IssuedRefreshToken(string RawToken, DateTime ExpiresAt);

    public enum RefreshRotationStatus
    {
        Success,
        Missing,
        Invalid,
        Expired,
        Inactive,
        ConcurrentReuse,
        ReuseDetected
    }

    public record RefreshRotationResult(RefreshRotationStatus Status, int UserId = 0, IssuedRefreshToken? NewToken = null)
    {
        public bool Succeeded => Status == RefreshRotationStatus.Success;
    }

    public static class RefreshTokenRevokeReasons
    {
        public const string Rotated = "Rotated";
        public const string Logout = "Logout";
        public const string Login = "Login";
        public const string ReuseDetected = "ReuseDetected";
        public const string PasswordChanged = "PasswordChanged";
        public const string PasswordReset = "PasswordReset";
        public const string AccountDisabled = "AccountDisabled";
        public const string UserInactive = "UserInactive";
        // Bật 2FA: các phiên tạo trước đó chưa từng qua 2FA => thu hồi hết, buộc đăng nhập lại bằng 2FA.
        public const string TwoFactorEnabled = "TwoFactorEnabled";
    }
}
