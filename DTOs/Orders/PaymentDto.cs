using ClothingAPI.Enums;

namespace ClothingAPI.DTOs.Orders;

public class PaymentDto
{
    public int PaymentId { get; set; }

    public PaymentMethod PaymentMethod { get; set; }

    public decimal Amount { get; set; }

    public PaymentStatus PaymentStatus { get; set; }

    public string? TransactionCode { get; set; }

    public DateTime? PaidAt { get; set; }

    public DateTime CreatedAt { get; set; }
}