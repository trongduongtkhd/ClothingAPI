using ClothingAPI.Enums;

namespace ClothingAPI.DTOs.Orders
{
    public class OrderSummaryDto
    {
        public int OrderId { get; set; }
        public string OrderCode { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public OrderStatus OrderStatus { get; set; }
        public PaymentStatus PaymentStatus { get; set; }
        public DateTime CreatedAt { get; set; }
        public int TotalItems { get; set; }
    }
}
