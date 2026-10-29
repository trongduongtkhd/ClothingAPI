using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using ClothingAPI.Data;
using ClothingAPI.DTOs.Orders;
using ClothingAPI.Enums;
using ClothingAPI.Exceptions;
using ClothingAPI.Helpers;
using ClothingAPI.Models;
using ClothingAPI.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClothingAPI.Services.Implementations;

public sealed class VnpayService(AppDbContext db, IOptions<VnpaySettings> options,
    IHttpClientFactory clients, TimeProvider clock, ILogger<VnpayService> logger) : IVnpayService
{
    private VnpaySettings Settings => options.Value;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static bool Open(PaymentAttempt a) => a.Status is PaymentAttemptStatus.Pending or PaymentAttemptStatus.NeedsReconciliation;
    private IQueryable<Order> Orders => db.Orders.Include(x => x.Payments).ThenInclude(x => x.Attempts);
    private static Payment PaymentOf(Order order) => order.Payments.SingleOrDefault(x => x.PaymentMethod == PaymentMethod.VnPay)
        ?? throw new BadRequestException("Đơn hàng không sử dụng VNPAY.");

    private async Task<Order> OwnedOrder(int userId, int orderId, bool tracking = true)
    {
        var query = tracking ? Orders : Orders.AsNoTracking();
        return await query.SingleOrDefaultAsync(x => x.OrderId == orderId && x.UserId == userId)
            ?? throw new NotFoundException("Không tìm thấy đơn hàng.");
    }

    public async Task<VnpayPaymentUrlDto> CreatePaymentUrlAsync(int userId, int orderId, string ipAddress, string? frontendOrigin = null)
    {
        if (!Settings.IsConfigured) throw new BadRequestException("VNPAY Sandbox chưa được cấu hình.");
        var returnUrl = Settings.ResolveFrontendOrigin(frontendOrigin) + "/api/payments/vnpay/return";
        await using var transaction = await db.Database.BeginTransactionAsync();
        await VnpayOrderLock.AcquireAsync(db, orderId);
        var order = await OwnedOrder(userId, orderId);
        var payment = PaymentOf(order);
        if (order.OrderStatus != OrderStatus.Pending || order.PaymentStatus is PaymentStatus.Paid or PaymentStatus.Refunded)
            throw new BadRequestException("Đơn hàng không còn có thể thanh toán.");
        var active = payment.Attempts.Where(Open).OrderByDescending(x => x.CreatedAt).FirstOrDefault();
        if (active is not null)
        {
            if (active.ExpiresAt > Now && !active.RequiresReview)
                return new(active.PaymentUrl, active.TxnRef, active.ExpiresAt);
            throw new BadRequestException("Giao dịch trước đang chờ xác minh. Vui lòng kiểm tra kết quả thanh toán trước khi thử lại.");
        }
        if (payment.Attempts.Any(x => x.RequiresReview))
            throw new BadRequestException("Giao dịch cần shop kiểm tra trước khi thanh toán lại.");
        var deadline = order.CreatedAt.AddMinutes(Settings.OrderMinutes);
        if (Now >= deadline) throw new BadRequestException("Đơn đã hết thời gian thanh toán. Vui lòng kiểm tra kết quả rồi hủy đơn và đặt lại.");
        var now = Now;
        var expiry = new[] { now.AddMinutes(Settings.PaymentMinutes), deadline }.Min();
        var attempt = new PaymentAttempt
        {
            PaymentId = payment.PaymentId,
            TxnRef = Guid.NewGuid().ToString("N"),
            Amount = order.TotalAmount,
            CreatedAt = now,
            ExpiresAt = expiry
        };
        var fields = new Dictionary<string, string>
        {
            ["vnp_Version"] = "2.1.0", ["vnp_Command"] = "pay", ["vnp_TmnCode"] = Settings.TmnCode,
            ["vnp_Amount"] = VnpayProtocol.Amount(attempt.Amount), ["vnp_CurrCode"] = "VND",
            ["vnp_TxnRef"] = attempt.TxnRef, ["vnp_OrderInfo"] = "Thanh toan don hang " + order.OrderCode,
            ["vnp_OrderType"] = "other", ["vnp_Locale"] = "vn", ["vnp_IpAddr"] = ipAddress,
            ["vnp_ReturnUrl"] = returnUrl, ["vnp_CreateDate"] = VnpayProtocol.Date(now),
            ["vnp_ExpireDate"] = VnpayProtocol.Date(expiry)
        };
        var data = VnpayProtocol.QueryString(fields);
        attempt.PaymentUrl = Settings.PaymentUrl + "?" + data + "&vnp_SecureHash=" + VnpayProtocol.Hash(Settings.HashSecret, data);
        db.PaymentAttempts.Add(attempt);
        payment.PaymentStatus = order.PaymentStatus = PaymentStatus.Unpaid;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return new(attempt.PaymentUrl, attempt.TxnRef, expiry);
    }

    public async Task<VnpayStatusDto> GetStatusAsync(int userId, int orderId)
    {
        var order = await OwnedOrder(userId, orderId, false);
        var payment = PaymentOf(order);
        var latest = payment.Attempts.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
        var active = payment.Attempts.Any(Open);
        var review = payment.Attempts.Any(x => x.RequiresReview);
        var canPay = Settings.IsConfigured && order.OrderStatus == OrderStatus.Pending
            && order.PaymentStatus is PaymentStatus.Unpaid or PaymentStatus.Failed
            && Now < order.CreatedAt.AddMinutes(Settings.OrderMinutes) && !review
            && (!active || (latest is not null && latest.ExpiresAt > Now && latest.Status == PaymentAttemptStatus.Pending));
        var canCancel = order.OrderStatus == OrderStatus.Pending
            && order.PaymentStatus is PaymentStatus.Unpaid or PaymentStatus.Failed && !active && !review;
        var message = review ? "Giao dịch cần shop đối soát. Vui lòng không thanh toán thêm."
            : order.PaymentStatus == PaymentStatus.Paid ? "Thanh toán VNPAY thành công."
            : order.OrderStatus == OrderStatus.Cancelled ? "Đơn hàng đã được hủy."
            : active ? "Đang chờ xác nhận từ VNPAY. Bạn có thể kiểm tra lại kết quả."
            : latest?.Status == PaymentAttemptStatus.Cancelled ? "Bạn đã hủy giao dịch VNPAY. Đơn chưa được thanh toán; bạn có thể thanh toán lại."
            : latest?.Status == PaymentAttemptStatus.Expired ? "Phiên thanh toán đã hết hạn. Đơn chưa được thanh toán."
            : latest is null ? "Đơn hàng chưa thanh toán."
            : "Giao dịch chưa thành công. Bạn có thể thử lại nếu đơn còn thời hạn.";
        return new(order.OrderId, order.OrderCode, order.TotalAmount, order.PaymentStatus.ToString(),
            latest?.Status.ToString(), payment.TransactionCode, payment.PaidAt, latest?.ExpiresAt,
            canPay, canCancel, review, message);
    }

    public async Task<VnpayIpnResult> HandleIpnAsync(IReadOnlyDictionary<string, string> fields)
    {
        if (!VnpayProtocol.Verify(Settings.HashSecret, fields)) return new("97", "Invalid signature");
        if (Value(fields, "vnp_TmnCode") != Settings.TmnCode) return new("97", "Invalid merchant");
        var found = await db.PaymentAttempts.AsNoTracking()
            .Where(x => x.TxnRef == Value(fields, "vnp_TxnRef"))
            .Select(x => new { x.Payment.OrderId }).SingleOrDefaultAsync();
        if (found is null) return new("01", "Order not found");
        await using var transaction = await db.Database.BeginTransactionAsync();
        await VnpayOrderLock.AcquireAsync(db, found.OrderId);
        var attempt = await db.PaymentAttempts.Include(x => x.Payment).ThenInclude(x => x.Order)
            .SingleAsync(x => x.TxnRef == Value(fields, "vnp_TxnRef"));
        if (!long.TryParse(Value(fields, "vnp_Amount"), NumberStyles.None, CultureInfo.InvariantCulture, out var amount)
            || amount != attempt.Amount * 100 || attempt.Amount != attempt.Payment.Order.TotalAmount)
            return new("04", "Invalid amount");
        if (Value(fields, "vnp_CurrCode") is { Length: > 0 } currency && currency != "VND")
            return new("04", "Invalid currency");
        var success = Value(fields, "vnp_ResponseCode") == "00" && Value(fields, "vnp_TransactionStatus") == "00";
        if (success && string.IsNullOrWhiteSpace(Value(fields, "vnp_TransactionNo"))) return new("99", "Missing transaction number");
        var already = attempt.Status == PaymentAttemptStatus.Succeeded;
        ApplyResult(attempt, fields, success, false);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return already ? new("02", "Order already confirmed") : new("00", "Confirm success");
    }

    public async Task<string> HandleReturnAsync(IReadOnlyDictionary<string, string> fields)
    {
        var invalid = Settings.ResultUrl + "?invalid=true";
        if (!VnpayProtocol.Verify(Settings.HashSecret, fields) || Value(fields, "vnp_TmnCode") != Settings.TmnCode) return invalid;
        var attempt = await db.PaymentAttempts.AsNoTracking().Where(x => x.TxnRef == Value(fields, "vnp_TxnRef"))
            .Select(x => new { x.Payment.OrderId, x.PaymentUrl }).SingleOrDefaultAsync();
        if (attempt is null) return invalid;
        var resultUrl = Settings.ResultUrlForAttempt(attempt.PaymentUrl);
        // Sandbox may not deliver IPN (and QUERYDR can be unavailable). A return with an authentic
        // gateway signature and matching merchant/ref/amount is processed through the same locked,
        // idempotent validator as IPN. A browser-provided response code alone is never sufficient.
        var result = await HandleIpnAsync(fields);
        if (result.RspCode is not "00" and not "02") return resultUrl + "?invalid=true";
        return resultUrl + "?orderId=" + attempt.OrderId;
    }

    public async Task<VnpayStatusDto> ReconcileAsync(int userId, int orderId)
    {
        var order = await OwnedOrder(userId, orderId, false);
        var latest = PaymentOf(order).Attempts.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
        if (latest is not null && Open(latest)) await QueryAttemptAsync(orderId, latest.PaymentAttemptId);
        return await GetStatusAsync(userId, orderId);
    }

    private async Task QueryAttemptAsync(int orderId, int attemptId)
    {
        if (!Settings.IsConfigured) return;
        PaymentAttempt snapshot;
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await VnpayOrderLock.AcquireAsync(db, orderId);
            var attempt = await db.PaymentAttempts.SingleAsync(x => x.PaymentAttemptId == attemptId);
            // VNPAY throttles transaction queries; persist the throttle across requests and workers.
            if (!Open(attempt) || attempt.LastQueriedAt > Now.AddMinutes(-5)) return;
            attempt.LastQueriedAt = Now;
            snapshot = attempt;
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        var fields = new Dictionary<string, string>
        {
            ["vnp_RequestId"] = Guid.NewGuid().ToString("N"), ["vnp_Version"] = "2.1.0",
            ["vnp_Command"] = "querydr", ["vnp_TmnCode"] = Settings.TmnCode,
            ["vnp_TxnRef"] = snapshot.TxnRef, ["vnp_TransactionDate"] = VnpayProtocol.Date(snapshot.CreatedAt),
            ["vnp_CreateDate"] = VnpayProtocol.Date(Now), ["vnp_IpAddr"] = "127.0.0.1",
            ["vnp_OrderInfo"] = "Kiem tra giao dich " + snapshot.TxnRef
        };
        string[] requestKeys = ["vnp_RequestId", "vnp_Version", "vnp_Command", "vnp_TmnCode", "vnp_TxnRef", "vnp_TransactionDate", "vnp_CreateDate", "vnp_IpAddr", "vnp_OrderInfo"];
        fields["vnp_SecureHash"] = VnpayProtocol.Hash(Settings.HashSecret, string.Join("|", requestKeys.Select(k => fields[k])));
        Dictionary<string, string> result;
        try
        {
            using var response = await clients.CreateClient("vnpay").PostAsJsonAsync(Settings.TransactionApiUrl, fields);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            result = document.RootElement.EnumerateObject().ToDictionary(x => x.Name,
                x => x.Value.ValueKind == JsonValueKind.String ? x.Value.GetString() ?? "" : x.Value.ToString());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning("VNPAY query unavailable for order {OrderId}, error {ErrorType}, HTTP {Status}; awaiting signed return or IPN",
                orderId, ex.GetType().Name, (ex as HttpRequestException)?.StatusCode);
            return;
        }
        string[] responseKeys = ["vnp_ResponseId", "vnp_Command", "vnp_ResponseCode", "vnp_Message", "vnp_TmnCode", "vnp_TxnRef", "vnp_Amount", "vnp_BankCode", "vnp_PayDate", "vnp_TransactionNo", "vnp_TransactionType", "vnp_TransactionStatus", "vnp_OrderInfo"];
        if (!VnpayProtocol.VerifyHash(Settings.HashSecret, string.Join("|", responseKeys.Select(k => Value(result, k))), Value(result, "vnp_SecureHash"))
            || Value(result, "vnp_TmnCode") != Settings.TmnCode || Value(result, "vnp_TxnRef") != snapshot.TxnRef
            || Value(result, "vnp_Command") != "querydr" || Value(result, "vnp_ResponseCode") != "00"
            || Value(result, "vnp_Amount") != VnpayProtocol.Amount(snapshot.Amount))
        {
            logger.LogWarning("VNPAY query not confirmed for order {OrderId}; keeping payment pending", orderId);
            return;
        }
        await using var update = await db.Database.BeginTransactionAsync();
        await VnpayOrderLock.AcquireAsync(db, orderId);
        // Another request may have processed IPN while the HTTP call was in flight.
        db.ChangeTracker.Clear();
        var current = await db.PaymentAttempts.Include(x => x.Payment).ThenInclude(x => x.Order)
            .SingleAsync(x => x.PaymentAttemptId == attemptId);
        var success = Value(result, "vnp_TransactionStatus") == "00";
        if (!success || !string.IsNullOrWhiteSpace(Value(result, "vnp_TransactionNo")))
            ApplyResult(current, result, success, true);
        await db.SaveChangesAsync();
        await update.CommitAsync();
    }

    private void ApplyResult(PaymentAttempt attempt, IReadOnlyDictionary<string, string> fields, bool success, bool query)
    {
        var payment = attempt.Payment;
        var order = payment.Order;
        // A stale failure must never downgrade a successful payment.
        if (attempt.Status == PaymentAttemptStatus.Succeeded) return;
        attempt.ResponseCode = Value(fields, "vnp_ResponseCode");
        attempt.TransactionStatus = Value(fields, "vnp_TransactionStatus");
        attempt.GatewayTransactionNo = Value(fields, "vnp_TransactionNo");
        attempt.BankCode = Value(fields, "vnp_BankCode");
        if (success)
        {
            attempt.Status = PaymentAttemptStatus.Succeeded;
            attempt.CompletedAt = Now;
            if (order.OrderStatus is OrderStatus.Cancelled or OrderStatus.Returned
                || payment.PaymentStatus == PaymentStatus.Refunded
                || (payment.PaymentStatus == PaymentStatus.Paid && payment.TransactionCode != attempt.GatewayTransactionNo))
            {
                attempt.RequiresReview = true;
                logger.LogError("VNPAY payment requires reconciliation for order {OrderId}, attempt {AttemptId}", order.OrderId, attempt.PaymentAttemptId);
                return;
            }
            payment.TransactionCode = attempt.GatewayTransactionNo;
            payment.PaidAt = ParsePayDate(Value(fields, "vnp_PayDate")) ?? Now;
            payment.PaymentStatus = order.PaymentStatus = PaymentStatus.Paid;
            order.UpdatedAt = Now;
            db.OrderStatusHistories.Add(new OrderStatusHistory
            {
                OrderId = order.OrderId, Status = order.OrderStatus, CreatedAt = Now,
                Note = "Đã nhận xác nhận thanh toán VNPAY."
            });
            return;
        }
        // Query success describes the query request; transaction status describes the payment.
        var status = Value(fields, "vnp_TransactionStatus");
        var response = Value(fields, "vnp_ResponseCode");
        if (status is "04" or "05" or "06" or "07" or "09" || (!query && response == "07"))
        {
            attempt.Status = PaymentAttemptStatus.NeedsReconciliation;
            attempt.RequiresReview = true;
            return;
        }
        if (query && status == "01" && Now < attempt.ExpiresAt.AddMinutes(5)) return;
        if ((query && status is not "01" and not "02") || (!query && status is not "02" && response is not "24" and not "11" and not "13" and not "51" and not "09" and not "10" and not "12" and not "65" and not "75" and not "79" and not "99"))
        {
            attempt.Status = PaymentAttemptStatus.NeedsReconciliation;
            return;
        }
        attempt.Status = (query && status == "01") || response == "11" ? PaymentAttemptStatus.Expired
            : response == "24" ? PaymentAttemptStatus.Cancelled : PaymentAttemptStatus.Failed;
        attempt.CompletedAt = Now;
        if (order.PaymentStatus is not PaymentStatus.Paid and not PaymentStatus.Refunded)
            payment.PaymentStatus = order.PaymentStatus = PaymentStatus.Failed;
    }

    public async Task ProcessExpiredOrdersAsync()
    {
        if (!Settings.IsConfigured) return;
        var ids = await db.Orders.AsNoTracking().Where(x => x.OrderStatus == OrderStatus.Pending
            && x.PaymentStatus != PaymentStatus.Paid && x.PaymentStatus != PaymentStatus.Refunded
            && x.CreatedAt < Now.AddMinutes(-Settings.OrderMinutes)
            && x.Payments.Any(p => p.PaymentMethod == PaymentMethod.VnPay))
            .OrderBy(x => x.CreatedAt).Take(25).Select(x => new { x.OrderId, x.UserId }).ToListAsync();
        foreach (var id in ids)
        {
            try
            {
                await ReconcileAsync(id.UserId, id.OrderId);
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync();
                await VnpayOrderLock.AcquireAsync(db, id.OrderId);
                var order = await Orders.Include(x => x.OrderItems).ThenInclude(x => x.Variant)
                    .Include(x => x.CouponUsages).ThenInclude(x => x.Coupon).SingleAsync(x => x.OrderId == id.OrderId);
                var payment = PaymentOf(order);
                if (order.OrderStatus != OrderStatus.Pending || order.PaymentStatus is PaymentStatus.Paid or PaymentStatus.Refunded
                    || payment.Attempts.Any(x => Open(x) || x.RequiresReview)) continue;
                foreach (var item in order.OrderItems) item.Variant.StockQuantity += item.Quantity;
                foreach (var usage in order.CouponUsages)
                    if (usage.Coupon.UsedCount > 0) usage.Coupon.UsedCount--;
                db.CouponUsages.RemoveRange(order.CouponUsages);
                order.OrderStatus = OrderStatus.Cancelled;
                order.UpdatedAt = Now;
                db.OrderStatusHistories.Add(new OrderStatusHistory
                {
                    OrderId = id.OrderId, Status = OrderStatus.Cancelled, CreatedAt = Now,
                    Note = "Tự hủy đơn VNPAY hết hạn thanh toán; đã hoàn tồn kho và lượt dùng mã."
                });
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception ex) { logger.LogWarning(ex, "Could not expire VNPAY order {OrderId}", id.OrderId); }
            finally { db.ChangeTracker.Clear(); }
        }
    }

    private static string Value(IReadOnlyDictionary<string, string> data, string key) => data.GetValueOrDefault(key) ?? "";
    private static DateTime? ParsePayDate(string date) => DateTime.TryParseExact(date, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
        DateTimeStyles.None, out var parsed) ? DateTime.SpecifyKind(parsed.AddHours(-7), DateTimeKind.Utc) : null;
}
