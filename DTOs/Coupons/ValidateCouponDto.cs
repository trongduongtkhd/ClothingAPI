using System.ComponentModel.DataAnnotations;
using ClothingAPI.DTOs.Orders;

namespace ClothingAPI.DTOs.Coupons
{
    public class ValidateCouponDto
    {
        [Required(ErrorMessage = "Mã giảm giá là bắt buộc.")]
        [StringLength(50)]
        public string Code { get; set; } = string.Empty;

        // Có giá trị => tính mã giảm giá trên sản phẩm "Mua ngay" thay vì giỏ hàng.
        public BuyNowItemDto? BuyNowItem { get; set; }
    }
}
