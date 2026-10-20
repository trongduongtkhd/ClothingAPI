namespace ClothingAPI.DTOs.Auth
{
    public class AuthResponseDto
    {
        public string AccessToken { get; set; } = string.Empty;

        public DateTime ExpiresAt { get; set; }

        public CurrentUserDto User { get; set; } = new();

        // Luôn false ở đây (đã có token). Khi cần 2FA, /auth/login trả TwoFactorChallengeDto với giá trị true.
        public bool RequiresTwoFactor { get; set; }
    }
}
