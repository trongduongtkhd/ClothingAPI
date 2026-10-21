using ClothingAPI.Enums;

namespace ClothingAPI.Models;

// Yêu cầu trả hàng / hoàn tiền của khách cho một đơn đã hoàn thành (mỗi đơn tối đa 1 yêu cầu).
public class ReturnRequest
{
    public int ReturnRequestId { get; set; }

    public int OrderId { get; set; }

    public int UserId { get; set; }

    public string Reason { get; set; } = string.Empty;

    public ReturnRequestStatus Status { get; set; } = ReturnRequestStatus.Pending;

    public string? AdminNote { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ProcessedAt { get; set; }

    public int? ProcessedByUserId { get; set; }

    public Order Order { get; set; } = null!;

    public User User { get; set; } = null!;

    public User? ProcessedByUser { get; set; }
}
