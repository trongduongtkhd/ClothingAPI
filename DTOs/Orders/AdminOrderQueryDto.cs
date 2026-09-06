using ClothingAPI.Enums;

namespace ClothingAPI.DTOs.Orders
{
    public class AdminOrderQueryDto // Admin quan ly don hang 
    {
        public string? Keyword { get; set; }

        public OrderStatus? Status { get; set; }

        public PaymentStatus? PaymentStatus { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 10;
    }
}
