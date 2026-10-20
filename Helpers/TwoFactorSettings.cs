namespace ClothingAPI.Helpers
{
    public class TwoFactorSettings
    {
        // Tên hiển thị trong Google Authenticator (tham số issuer của otpauth://).
        public string Issuer { get; set; } = "ClothingStore";

        // Thời gian sống của "challenge" (phiên chờ nhập mã sau khi mật khẩu đúng).
        public int ChallengeMinutes { get; set; } = 5;

        // Số lần nhập sai tối đa trên MỘT challenge; giới hạn theo user nằm ở AccountLockout.
        public int MaxChallengeAttempts { get; set; } = 5;

        public int RecoveryCodeCount { get; set; } = 8;

        // Số bước thời gian (30 giây) được lệch về trước/sau để bù lệch đồng hồ điện thoại.
        public int VerificationWindow { get; set; } = 1;

        // Khóa HMAC cho mã khôi phục: lộ DB mà không lộ pepper thì không dò ngược được hash.
        // Mất hoặc đổi pepper => mọi mã khôi phục đã phát không dùng được nữa.
        public string RecoveryCodePepper { get; set; } = string.Empty;
    }
}
