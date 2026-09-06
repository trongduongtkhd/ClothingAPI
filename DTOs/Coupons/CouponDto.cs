using ClothingAPI.Enums;

namespace ClothingAPI.DTOs.Coupons;

public class CouponDto
{
    public int CouponId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DiscountType DiscountType { get; set; }

    public decimal DiscountValue { get; set; }

    public decimal? MaxDiscountAmount { get; set; }

    public decimal MinOrderAmount { get; set; }

    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    public int? UsageLimit { get; set; }

    public int? UsageLimitPerUser { get; set; }

    public int UsedCount { get; set; }

    public bool IsActive { get; set; }

    public List<int> CategoryIds { get; set; } = [];

    public List<int> ProductIds { get; set; } = [];
}