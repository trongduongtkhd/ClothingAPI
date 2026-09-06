using System.ComponentModel.DataAnnotations;

namespace ClothingAPI.DTOs.Reviews
{
    public class CreateReviewDto
    {
        [Range(1, int.MaxValue, ErrorMessage = "Order item không hợp lệ.")]
        public int OrderItemId { get; set; }

        [Range(1, 5, ErrorMessage = "Đánh giá phải từ 1 đến 5.")]
        public byte Rating { get; set; }

        [StringLength(1000)]
        public string? Comment { get; set; }
    }
}
