using System.ComponentModel.DataAnnotations;
using ClothingAPI.Enums;

namespace ClothingAPI.DTOs.Orders
{
    public class ReturnRequestDto
    {
        public int ReturnRequestId { get; set; }

        public string Reason { get; set; } = string.Empty;

        public ReturnRequestStatus Status { get; set; }

        public string? AdminNote { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? ProcessedAt { get; set; }
    }

    public class CreateReturnRequestDto
    {
        [Required(ErrorMessage = "Vui lòng nhập lý do trả hàng.")]
        [StringLength(500, MinimumLength = 10, ErrorMessage = "Lý do trả hàng phải từ 10 đến 500 ký tự.")]
        public string Reason { get; set; } = string.Empty;
    }

    public class ProcessReturnRequestDto
    {
        [StringLength(500)]
        public string? Note { get; set; }
    }
}
