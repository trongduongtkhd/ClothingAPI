namespace ClothingAPI.DTOs.Reviews
{
    public class MyReviewDto
    {
        public int ReviewId { get; set; }

        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;

        public int? OrderItemId { get; set; }

        public byte Rating { get; set; }
        public string? Comment { get; set; }

        public bool IsApproved { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
