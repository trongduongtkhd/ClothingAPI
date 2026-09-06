using ClothingAPI.Enums;

namespace ClothingAPI.DTOs.Orders
{
    public class AdminOrderSummaryDto
    {
        public int OrderId { get; set; }

        public string OrderCode { get; set; } = string.Empty;

        public string CustomerName { get; set; } = string.Empty;

        public string CustomerEmail { get; set; } = string.Empty;

        public string ReceiverName { get; set; } = string.Empty;

        public decimal TotalAmount { get; set; }

        public OrderStatus OrderStatus { get; set; }

        public PaymentStatus PaymentStatus { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
