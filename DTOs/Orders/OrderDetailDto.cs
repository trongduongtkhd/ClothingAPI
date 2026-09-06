using ClothingAPI.Enums;

namespace ClothingAPI.DTOs.Orders;

public class OrderDetailDto
{
    public int OrderId { get; set; }

    public string OrderCode { get; set; } = string.Empty;

    public string ReceiverName { get; set; } = string.Empty;

    public string ReceiverPhone { get; set; } = string.Empty;

    public string ShippingAddress { get; set; } = string.Empty;

    public string? Note { get; set; }

    public decimal Subtotal { get; set; }

    public decimal ShippingFee { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal TotalAmount { get; set; }

    public string? CouponCode { get; set; }

    public OrderStatus OrderStatus { get; set; }

    public PaymentStatus PaymentStatus { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<OrderItemDto> Items { get; set; } = [];

    public List<PaymentDto> Payments { get; set; } = [];

    public List<OrderStatusHistoryDto> StatusHistories { get; set; } = [];
}