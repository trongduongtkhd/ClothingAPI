using ClothingAPI.Enums;

namespace ClothingAPI.Models;

public class PaymentAttempt
{
    public int PaymentAttemptId { get; set; }
    public int PaymentId { get; set; }
    public string TxnRef { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public PaymentAttemptStatus Status { get; set; } = PaymentAttemptStatus.Pending;
    public string PaymentUrl { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? LastQueriedAt { get; set; }
    public string? GatewayTransactionNo { get; set; }
    public string? BankCode { get; set; }
    public string? ResponseCode { get; set; }
    public string? TransactionStatus { get; set; }
    public bool RequiresReview { get; set; }
    public Payment Payment { get; set; } = null!;
}
