using ClothingAPI.Enums;

namespace ClothingAPI.Models;

public class Payment
{
    public int PaymentId { get; set; }
    public int OrderId { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public string? TransactionCode { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Order Order { get; set; } = null!;
}