namespace ClothingAPI.DTOs.Reviews
{
    public class PublicReviewDto
    {
        public int ReviewId { get; set; }

        public string UserFullName { get; set; } = string.Empty;

        public byte Rating { get; set; }

        public string? Comment { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
