using System.ComponentModel.DataAnnotations;

namespace ClothingAPI.DTOs.Orders
{
    public class CancelOrderDto
    {
        [StringLength(500)]
        public string? Reason { get; set; }
    }
}
