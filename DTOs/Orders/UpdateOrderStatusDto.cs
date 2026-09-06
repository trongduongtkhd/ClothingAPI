using ClothingAPI.Enums;
using System.ComponentModel.DataAnnotations;

namespace ClothingAPI.DTOs.Orders
{
    public class UpdateOrderStatusDto
    {
        public OrderStatus Status { get; set; }

        [StringLength(500)]
        public string? Note { get; set; }
    }
}
