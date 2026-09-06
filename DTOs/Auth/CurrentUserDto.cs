namespace ClothingAPI.DTOs.Auth
{
    public class CurrentUserDto
    {
        public int UserId { get; set; }

        public string FullName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string? PhoneNumber { get; set; }

        public string? AvatarUrl { get; set; }

        public List<string> Roles { get; set; } = [];
    }
}
