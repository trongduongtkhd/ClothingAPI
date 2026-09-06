using ClothingAPI.Enums;
using System.ComponentModel.DataAnnotations;

namespace ClothingAPI.DTOs.Orders
{
    public class UpdatePaymentStatusDto
    {
        public PaymentStatus PaymentStatus { get; set; }

        [StringLength(500)]
        public string? Note { get; set; }
    }
}
