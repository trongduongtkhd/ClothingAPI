namespace ClothingAPI.DTOs.Coupons
{
    public class CouponValidationDto
    {
        public int CouponId { get; set; }

        public string CouponCode { get; set; } = string.Empty;

        public string CouponName { get; set; } = string.Empty;

        public decimal Subtotal { get; set; }

        public decimal EligibleSubtotal { get; set; }

        public decimal DiscountAmount { get; set; }

        public decimal AmountAfterDiscount { get; set; }
    }
}
