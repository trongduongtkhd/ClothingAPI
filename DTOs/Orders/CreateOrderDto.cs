using System.ComponentModel.DataAnnotations;
using ClothingAPI.Enums;

namespace ClothingAPI.DTOs.Orders;

public class CreateOrderDto
{
    [Range(1, int.MaxValue, ErrorMessage = "Địa chỉ giao hàng không hợp lệ.")]
    public int AddressId { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    [StringLength(50)]
    public string? CouponCode { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}