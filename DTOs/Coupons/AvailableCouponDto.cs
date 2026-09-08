using ClothingAPI.Enums;

namespace ClothingAPI.DTOs.Coupons
{
    public class AvailableCouponDto
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
        public int UserUsedCount { get; set; }

        public decimal EligibleSubtotal { get; set; }
        public decimal DiscountAmount { get; set; }

        public bool IsUsable { get; set; }
        public string? UnavailableReason { get; set; }
    }
}
