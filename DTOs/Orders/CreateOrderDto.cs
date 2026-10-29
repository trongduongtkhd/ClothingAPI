using System.ComponentModel.DataAnnotations;
using ClothingAPI.Enums;

namespace ClothingAPI.DTOs.Orders;

public class CreateOrderDto
{
    [Required(ErrorMessage = "Mã yêu cầu đặt hàng là bắt buộc.")]
    public Guid? RequestId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Địa chỉ giao hàng không hợp lệ.")]
    public int AddressId { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    [StringLength(50)]
    public string? CouponCode { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }

    // Có giá trị => "Mua ngay": chỉ đặt đúng sản phẩm này, không đụng giỏ hàng.
    public BuyNowItemDto? BuyNowItem { get; set; }
}
