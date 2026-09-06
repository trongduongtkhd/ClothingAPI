namespace ClothingAPI.DTOs.Coupons;

public class CouponUsageDto
{
    public int CouponUsageId { get; set; }

    public int UserId { get; set; }

    public string UserFullName { get; set; } = string.Empty;

    public string UserEmail { get; set; } = string.Empty;

    public int OrderId { get; set; }

    public string OrderCode { get; set; } = string.Empty;

    public decimal DiscountAmount { get; set; }

    public DateTime UsedAt { get; set; }
}