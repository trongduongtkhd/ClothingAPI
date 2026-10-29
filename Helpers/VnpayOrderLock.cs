using ClothingAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace ClothingAPI.Helpers;

// All VNPAY writers (IPN, retry, expiry and cancellation) acquire this transaction-owned lock.
// SQL Server releases it on commit/rollback, including when the process terminates.
public static class VnpayOrderLock
{
    public static async Task AcquireAsync(AppDbContext db, int orderId)
    {
        if (!db.Database.IsSqlServer()) return; // SQLite tests serialize their write transactions.
        var resource = $"vnpay-order-{orderId}";
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r = sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r < 0 THROW 51000, 'Payment lock unavailable', 1;");
    }

    public static async Task EnsureNoActiveAttemptAsync(AppDbContext db, int orderId)
    {
        if (await db.PaymentAttempts.AnyAsync(x => x.Payment.OrderId == orderId
            && (x.Status == Enums.PaymentAttemptStatus.Pending || x.Status == Enums.PaymentAttemptStatus.NeedsReconciliation)))
            throw new Exceptions.BadRequestException("Giao dịch VNPAY đang chờ kết quả. Vui lòng kiểm tra thanh toán trước khi hủy đơn.");
    }
}
