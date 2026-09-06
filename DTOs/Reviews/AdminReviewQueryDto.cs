namespace ClothingAPI.DTOs.Reviews
{
    public class AdminReviewQueryDto
    {
        public bool? IsApproved { get; set; }

        public string? Keyword { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 10;
    }
}
