namespace ClothingAPI.DTOs.Users
{
    public class AdminUserQueryDto
    {
        public string? Keyword { get; set; }

        public string? Role { get; set; }

        public bool? IsActive { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 10;
    }
}
