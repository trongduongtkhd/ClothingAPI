namespace ClothingAPI.DTOs.Auth
{
    public class AuthResponseDto
    {
        public string AccessToken { get; set; } = string.Empty;

        public DateTime ExpiresAt { get; set; }

        public CurrentUserDto User { get; set; } = new();
    }
}
