using System.Text.Json.Serialization;

namespace ClothingAPI.DTOs.Orders;

public record VnpayPaymentUrlDto(string PaymentUrl, string TxnRef, DateTime ExpiresAt);

public record VnpayStatusDto(int OrderId, string OrderCode, decimal Amount, string PaymentStatus,
    string? AttemptStatus, string? TransactionCode, DateTime? PaidAt, DateTime? ExpiresAt,
    bool CanPay, bool CanCancel, bool RequiresReview, string Message);

public record VnpayIpnResult(
    [property: JsonPropertyName("RspCode")] string RspCode,
    [property: JsonPropertyName("Message")] string Message);
