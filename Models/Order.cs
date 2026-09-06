using ClothingAPI.Enums;

namespace ClothingAPI.Models;

public class Order
{
    public int OrderId { get; set; }

    public string OrderCode { get; set; } = string.Empty;

    public int UserId { get; set; }

    public string ReceiverName { get; set; } = string.Empty;

    public string ReceiverPhone { get; set; } = string.Empty;

    public string ShippingAddress { get; set; } = string.Empty;

    public string? Note { get; set; }

    public decimal Subtotal { get; set; }

    public decimal ShippingFee { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal TotalAmount { get; set; }

    public int? CouponId { get; set; }

    public string? CouponCode { get; set; }

    public OrderStatus OrderStatus { get; set; }

    public PaymentStatus PaymentStatus { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public User User { get; set; } = null!;

    public Coupon? Coupon { get; set; }

    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();

    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<OrderStatusHistory> OrderStatusHistories { get; set; } = new List<OrderStatusHistory>();
    public ICollection<CouponUsage> CouponUsages { get; set; } = new List<CouponUsage>();

}