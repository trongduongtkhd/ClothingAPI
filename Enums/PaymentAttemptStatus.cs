namespace ClothingAPI.Enums;

public enum PaymentAttemptStatus
{
    Pending = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
    Expired = 5,
    NeedsReconciliation = 6
}
