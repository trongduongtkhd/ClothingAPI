namespace ClothingAPI.Helpers
{
    public class TurnstileSettings
    {
        // Secret key của widget Turnstile (Cloudflare Dashboard → Turnstile → widget → Settings).
        // TUYỆT ĐỐI không ghi ra log; đặt qua User Secrets (local) hoặc TURNSTILE_SECRET_KEY trong .env (Docker).
        public string SecretKey { get; set; } = string.Empty;

        // Endpoint xác minh token. Chỉ đổi khi kiểm thử (giả lập không gọi được Cloudflare).
        public string VerifyUrl { get; set; } = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

        // Quá thời gian này coi như không gọi được Cloudflare (login cho qua, register chặn).
        public int TimeoutSeconds { get; set; } = 5;

        // Tắt CAPTCHA (chỉ dùng khi dev/test). Khi tắt, mọi token đều được coi là hợp lệ.
        public bool Enabled { get; set; } = true;
    }
}
