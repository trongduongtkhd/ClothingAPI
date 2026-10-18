namespace ClothingAPI.Helpers
{
    public class RefreshTokenSettings
    {
        // Hạn tuyệt đối của một phiên đăng nhập: token xoay vòng kế thừa ExpiresAt của token cũ.
        public int ExpirationDays { get; set; } = 7;

        public string CookieName { get; set; } = "refreshToken";

        // Trình duyệt chỉ gửi cookie tới các endpoint /api/auth/*, không gửi kèm mọi request API.
        public string CookiePath { get; set; } = "/api/auth";

        // Local dev chạy HTTP thì tắt; Docker/production bật.
        public bool CookieSecure { get; set; } = true;

        // Token vừa bị xoay trong N giây mà bị gửi lại (2 tab / F5 liên tục) thì chỉ từ chối, không coi là bị đánh cắp.
        // Đặt 0 để tắt khi demo reuse detection.
        public int ReuseGraceSeconds { get; set; } = 10;
    }
}
