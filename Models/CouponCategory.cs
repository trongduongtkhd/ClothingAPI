namespace ClothingAPI.Models
{
    public class CouponCategory
    {
        public int CouponId { get; set; }

        public int CategoryId { get; set; }

        public Coupon Coupon { get; set; } = null!;

        public Category Category { get; set; } = null!;
    }
}
