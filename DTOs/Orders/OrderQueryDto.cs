using ClothingAPI.Enums;

namespace ClothingAPI.DTOs.Orders
{
    public class OrderQueryDto
    {
        public OrderStatus? Status { get; set; }

        public int Page { get; set; } = 1;

        public int PageSize { get; set; } = 10;
    }
}
