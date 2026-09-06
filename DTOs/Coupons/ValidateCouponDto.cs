using System.ComponentModel.DataAnnotations;

namespace ClothingAPI.DTOs.Coupons
{
    public class ValidateCouponDto
    {
        [Required(ErrorMessage = "Mã giảm giá là bắt buộc.")]
        [StringLength(50)]
        public string Code { get; set; } = string.Empty;  
    }
}
