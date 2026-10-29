using System.Net;
using System.Text;
using System.Text.Json;
using ClothingAPI.Data;
using ClothingAPI.DTOs.Orders;
using ClothingAPI.Enums;
using ClothingAPI.Exceptions;
using ClothingAPI.Helpers;
using ClothingAPI.Models;
using ClothingAPI.Services.Implementations;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace ClothingAPI.UnitTests;

public class VnpayTests : IDisposable
{
    private readonly TestDb data = new();
    private readonly ManualTimeProvider clock = new(DateTimeOffset.UtcNow);
    private readonly VnpaySettings settings = new()
    {
        Enabled = true, TmnCode = "TESTONLY", HashSecret = "unit-test-secret-no-real-credentials",
        PublicBaseUrl = "https://shop.example.test"
    };
    private readonly Mock<IHttpClientFactory> clients = new();

    private int NewOrder(PaymentStatus status = PaymentStatus.Unpaid)
    {
        var (variant, _) = data.AddVariant(stock: 9);
        return data.AddOrder(data.CustomerId, OrderStatus.Pending, status, [(variant, 1)], paymentMethod: PaymentMethod.VnPay).OrderId;
    }

    private VnpayService Service(AppDbContext db) => new(db, Options.Create(settings), clients.Object, clock, NullLogger<VnpayService>.Instance);

    private async Task<string> Attempt(int orderId)
    {
        await using var db = data.NewContext();
        return (await Service(db).CreatePaymentUrlAsync(data.CustomerId, orderId, "127.0.0.1")).TxnRef;
    }

    private Dictionary<string, string> Callback(string reference, string code = "00", string status = "00", string amount = "20000000", string transaction = "12345678")
    {
        var fields = new Dictionary<string, string>
        {
            ["vnp_TmnCode"] = settings.TmnCode, ["vnp_TxnRef"] = reference, ["vnp_Amount"] = amount,
            ["vnp_ResponseCode"] = code, ["vnp_TransactionStatus"] = status,
            ["vnp_TransactionNo"] = transaction, ["vnp_BankCode"] = "NCB", ["vnp_PayDate"] = VnpayProtocol.Date(clock.UtcNow)
        };
        Sign(fields);
        return fields;
    }

    private void Sign(Dictionary<string, string> fields) => fields["vnp_SecureHash"] = VnpayProtocol.Hash(settings.HashSecret, VnpayProtocol.QueryString(fields));

    [Fact]
    public void EncodingAmountAndVietnamTimeMatchProtocol()
    {
        Assert.Equal("vnp_A=a+b%2Bc&vnp_Z=100", VnpayProtocol.QueryString(new Dictionary<string, string>
        { ["vnp_Z"] = "100", ["vnp_A"] = "a b+c", ["vnp_SecureHash"] = "ignored", ["other"] = "ignored" }));
        Assert.Equal("76680000", VnpayProtocol.Amount(766800));
        Assert.Equal("20261025070000", VnpayProtocol.Date(new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc)));
        Assert.Throws<BadRequestException>(() => VnpayProtocol.Amount(4999));
        Assert.Throws<BadRequestException>(() => VnpayProtocol.Amount(5000.01m));
    }

    [Fact]
    public void HmacMatchesRfc4231Vector()
    {
        var expected = "87aa7cdea5ef619d4ff0b4241a1d6cb02379f4e2ce4ec2787ad0b30545e17cde" +
            "daa833b7d6b8a702038b274eaea3f4e4be9d914eeb61f1702e696c203a126854";
        Assert.Equal(expected, VnpayProtocol.Hash(new string('\v', 20), "Hi There"));
    }

    [Fact]
    public async Task CreatesSignedUrlAndReusesActiveAttemptWithoutChangingStock()
    {
        var id = NewOrder();
        var before = data.Query(x => x.ProductVariants.Sum(v => v.StockQuantity));
        await using var db = data.NewContext();
        var service = Service(db);
        var first = await service.CreatePaymentUrlAsync(data.CustomerId, id, "127.0.0.1");
        var second = await service.CreatePaymentUrlAsync(data.CustomerId, id, "127.0.0.1");
        Assert.Equal(first, second);
        Assert.Single(await db.PaymentAttempts.ToListAsync());
        var parsed = QueryStringFields(first.PaymentUrl);
        Assert.True(VnpayProtocol.Verify(settings.HashSecret, parsed));
        Assert.Equal(settings.ReturnUrl, parsed["vnp_ReturnUrl"]);
        Assert.Equal("20000000", parsed["vnp_Amount"]);
        Assert.Equal(before, data.Query(x => x.ProductVariants.Sum(v => v.StockQuantity)));
    }

    [Fact]
    public async Task CannotPayAnotherUsersOrderOrCodOrder()
    {
        var id = NewOrder();
        await using var db = data.NewContext();
        await Assert.ThrowsAsync<NotFoundException>(() => Service(db).CreatePaymentUrlAsync(data.AdminId, id, "127.0.0.1"));
        var cod = data.AddOrder(data.CustomerId, OrderStatus.Pending).OrderId;
        await Assert.ThrowsAsync<BadRequestException>(() => Service(db).CreatePaymentUrlAsync(data.CustomerId, cod, "127.0.0.1"));
    }

    [Theory]
    [InlineData(PaymentStatus.Paid)]
    [InlineData(PaymentStatus.Refunded)]
    public async Task CannotStartPaymentOnSettledOrder(PaymentStatus status)
    {
        var id = NewOrder(status);
        await using var db = data.NewContext();
        await Assert.ThrowsAsync<BadRequestException>(() => Service(db).CreatePaymentUrlAsync(data.CustomerId, id, "127.0.0.1"));
    }

    [Fact]
    public async Task SuccessRecordsGatewayTransactionAndDuplicateIsIdempotent()
    {
        var id = NewOrder(); var reference = await Attempt(id);
        await using var db = data.NewContext(); var service = Service(db);
        Assert.Equal("00", (await service.HandleIpnAsync(Callback(reference))).RspCode);
        Assert.Equal("02", (await service.HandleIpnAsync(Callback(reference))).RspCode);
        Assert.Equal(PaymentStatus.Paid, data.Query(x => x.Orders.Single(o => o.OrderId == id).PaymentStatus));
        Assert.Equal("12345678", data.Query(x => x.Payments.Single(p => p.OrderId == id).TransactionCode));
        Assert.Single(await db.OrderStatusHistories.Where(x => x.OrderId == id).ToListAsync());
    }

    [Theory]
    [InlineData("signature", "97")]
    [InlineData("merchant", "97")]
    [InlineData("amount", "04")]
    [InlineData("unknown", "01")]
    public async Task RejectsInvalidCallback(string invalid, string expected)
    {
        var id = NewOrder(); var reference = await Attempt(id); var fields = Callback(reference);
        if (invalid == "signature") fields["vnp_SecureHash"] = "bad";
        if (invalid == "merchant") { fields["vnp_TmnCode"] = "OTHER"; Sign(fields); }
        if (invalid == "amount") { fields["vnp_Amount"] = "1"; Sign(fields); }
        if (invalid == "unknown") { fields["vnp_TxnRef"] = "missing"; Sign(fields); }
        await using var db = data.NewContext();
        Assert.Equal(expected, (await Service(db).HandleIpnAsync(fields)).RspCode);
        Assert.Equal(PaymentStatus.Unpaid, data.Query(x => x.Orders.Single(o => o.OrderId == id).PaymentStatus));
    }

    [Fact]
    public async Task FailureAllowsRetryWithNewReferenceAndDoesNotDowngradeSuccess()
    {
        var id = NewOrder(); var first = await Attempt(id);
        await using (var db = data.NewContext()) await Service(db).HandleIpnAsync(Callback(first, "24", "02"));
        var second = await Attempt(id); Assert.NotEqual(first, second);
        await using (var db = data.NewContext()) await Service(db).HandleIpnAsync(Callback(second));
        await using (var db = data.NewContext()) await Service(db).HandleIpnAsync(Callback(first, "24", "02"));
        Assert.Equal(PaymentStatus.Paid, data.Query(x => x.Orders.Single(o => o.OrderId == id).PaymentStatus));
        Assert.Equal(2, data.Query(x => x.PaymentAttempts.Count()));
    }

    [Fact]
    public async Task SignedReturnMarksPaidWithoutIpnOrQueryAndDuplicateDoesNotRepeatHistory()
    {
        var id = NewOrder(); var reference = await Attempt(id);
        await using var db = data.NewContext(); var service = Service(db);
        Assert.Equal(settings.ResultUrl + "?orderId=" + id, await service.HandleReturnAsync(Callback(reference)));
        Assert.Equal(settings.ResultUrl + "?orderId=" + id, await service.HandleReturnAsync(Callback(reference)));
        Assert.Equal(PaymentStatus.Paid, data.Query(x => x.Orders.Single(o => o.OrderId == id).PaymentStatus));
        Assert.Equal(1, data.Query(x => x.OrderStatusHistories.Count(o => o.OrderId == id)));
        clients.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancelledReturnClosesAttemptAndRetryUsesNewReference()
    {
        var id = NewOrder(); var reference = await Attempt(id);
        await using (var db = data.NewContext())
        {
            var service = Service(db);
            await service.HandleReturnAsync(Callback(reference, "24", "02", transaction: "0"));
            var status = await service.GetStatusAsync(data.CustomerId, id);
            Assert.Equal("Cancelled", status.AttemptStatus);
            Assert.True(status.CanPay);
            Assert.Contains("hủy", status.Message);
        }
        Assert.NotEqual(reference, await Attempt(id));
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("amount")]
    [InlineData("merchant")]
    public async Task InvalidReturnNeverMarksPaid(string invalid)
    {
        var id = NewOrder(); var reference = await Attempt(id);
        var fields = Callback(reference);
        if (invalid == "signature") fields["vnp_SecureHash"] = "bad";
        if (invalid == "amount") { fields["vnp_Amount"] = "1"; Sign(fields); }
        if (invalid == "merchant") { fields["vnp_TmnCode"] = "EVIL"; Sign(fields); }
        await using var db = data.NewContext();
        Assert.EndsWith("?invalid=true", await Service(db).HandleReturnAsync(fields));
        Assert.Equal(PaymentStatus.Unpaid, data.Query(x => x.Orders.Single(o => o.OrderId == id).PaymentStatus));
    }

    [Theory]
    [InlineData("http://localhost:4200")]
    [InlineData("https://shop.example.test")]
    public async Task ReturnUsesStartingFrontendOriginAndIgnoresUntrustedRedirect(string origin)
    {
        var id = NewOrder();
        await using var db = data.NewContext(); var service = Service(db);
        var url = await service.CreatePaymentUrlAsync(data.CustomerId, id, "127.0.0.1", origin);
        Assert.Equal(origin + "/api/payments/vnpay/return", QueryStringFields(url.PaymentUrl)["vnp_ReturnUrl"]);
        var callback = Callback(url.TxnRef);
        callback["redirect"] = "https://evil.example/";
        Assert.Equal(origin + "/checkout/vnpay-result?orderId=" + id, await service.HandleReturnAsync(callback));
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("http://localhost:4200.evil.example")]
    [InlineData("https://shop.example.test@evil.example")]
    public async Task UnapprovedFrontendOriginCannotBeUsedForRedirect(string origin)
    {
        var id = NewOrder(); await using var db = data.NewContext();
        await Assert.ThrowsAsync<BadRequestException>(() => Service(db).CreatePaymentUrlAsync(data.CustomerId, id, "127.0.0.1", origin));
        Assert.Equal(0, data.Query(x => x.PaymentAttempts.Count()));
    }

    [Fact]
    public async Task SignedReturnStillWorksAfterQueryApiIsForbidden()
    {
        var id = NewOrder(); var reference = await Attempt(id);
        clients.Setup(x => x.CreateClient("vnpay")).Returns(new HttpClient(new ForbiddenHandler()));
        await using var db = data.NewContext(); var service = Service(db);
        Assert.Equal("Unpaid", (await service.ReconcileAsync(data.CustomerId, id)).PaymentStatus);
        await service.HandleReturnAsync(Callback(reference));
        Assert.Equal("Paid", (await service.GetStatusAsync(data.CustomerId, id)).PaymentStatus);
    }

    [Fact]
    public async Task ActiveAttemptBlocksCancellationAndExpiredAttemptBlocksNewUrlUntilReconciled()
    {
        var id = NewOrder(); await Attempt(id);
        await using var db = data.NewContext();
        var orders = new OrderService(db, Mock.Of<Services.Interfaces.ICouponService>());
        await Assert.ThrowsAsync<BadRequestException>(() => orders.CancelAsync(data.CustomerId, id, new CancelOrderDto()));
        clock.Advance(TimeSpan.FromMinutes(16));
        await Assert.ThrowsAsync<BadRequestException>(() => Service(db).CreatePaymentUrlAsync(data.CustomerId, id, "127.0.0.1"));
        Assert.Equal(1, data.Query(x => x.PaymentAttempts.Count()));
    }

    [Fact]
    public async Task ManualPaidShippingAndRefundAreBlockedForVnpay()
    {
        var id = NewOrder();
        await using var db = data.NewContext(); var admin = new AdminOrderService(db);
        await Assert.ThrowsAsync<BadRequestException>(() => admin.UpdatePaymentStatusAsync(data.AdminId, id, new UpdatePaymentStatusDto { PaymentStatus = PaymentStatus.Paid }));
        await Assert.ThrowsAsync<BadRequestException>(() => admin.UpdateOrderStatusAsync(data.AdminId, id, new UpdateOrderStatusDto { Status = OrderStatus.Confirmed }));
        var paid = NewOrder(PaymentStatus.Paid);
        await Assert.ThrowsAsync<BadRequestException>(() => admin.UpdateOrderStatusAsync(data.AdminId, paid, new UpdateOrderStatusDto { Status = OrderStatus.Cancelled, Note = "Hủy để kiểm tra" }));
    }

    [Fact]
    public async Task LateSuccessOnCancelledOrderIsRecordedForReviewWithoutReopeningOrder()
    {
        var id = NewOrder(); var reference = await Attempt(id);
        await using (var db = data.NewContext())
        {
            db.Orders.Single(o => o.OrderId == id).OrderStatus = OrderStatus.Cancelled;
            await db.SaveChangesAsync();
        }
        await using (var db = data.NewContext()) await Service(db).HandleIpnAsync(Callback(reference));
        Assert.Equal(OrderStatus.Cancelled, data.Query(x => x.Orders.Single(o => o.OrderId == id).OrderStatus));
        Assert.True(data.Query(x => x.PaymentAttempts.Single().RequiresReview));
    }

    [Fact]
    public async Task SignedQueryCanConfirmMissingIpnAndIsThrottled()
    {
        var id = NewOrder(); var reference = await Attempt(id); var calls = 0;
        var handler = new QueryHandler(() =>
        {
            calls++;
            var fields = Callback(reference);
            fields["vnp_ResponseId"] = "response1"; fields["vnp_Command"] = "querydr";
            fields["vnp_Message"] = "Success"; fields["vnp_TransactionType"] = "01"; fields["vnp_OrderInfo"] = "Kiem tra";
            string[] keys = ["vnp_ResponseId", "vnp_Command", "vnp_ResponseCode", "vnp_Message", "vnp_TmnCode", "vnp_TxnRef", "vnp_Amount", "vnp_BankCode", "vnp_PayDate", "vnp_TransactionNo", "vnp_TransactionType", "vnp_TransactionStatus", "vnp_OrderInfo"];
            fields["vnp_SecureHash"] = VnpayProtocol.Hash(settings.HashSecret, string.Join("|", keys.Select(k => fields.GetValueOrDefault(k) ?? "")));
            return fields;
        });
        clients.Setup(x => x.CreateClient("vnpay")).Returns(new HttpClient(handler));
        await using var db = data.NewContext(); var service = Service(db);
        Assert.Equal("Paid", (await service.ReconcileAsync(data.CustomerId, id)).PaymentStatus);
        await service.ReconcileAsync(data.CustomerId, id);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task InvalidQueryResponseNeverMarksPaidOrExpiresOrder()
    {
        var id = NewOrder(); await Attempt(id);
        clients.Setup(x => x.CreateClient("vnpay")).Returns(new HttpClient(new QueryHandler(() => new() { ["vnp_ResponseCode"] = "00", ["vnp_TransactionStatus"] = "00" })));
        await using var db = data.NewContext();
        var result = await Service(db).ReconcileAsync(data.CustomerId, id);
        Assert.Equal("Unpaid", result.PaymentStatus);
        Assert.Equal("Pending", result.AttemptStatus);
    }

    [Fact]
    public async Task WorkerCancelsUnstartedExpiredOrderAndRestoresStockOnce()
    {
        var id = NewOrder(); var before = data.Query(x => x.ProductVariants.Sum(v => v.StockQuantity));
        clock.Advance(TimeSpan.FromMinutes(31));
        await using var db = data.NewContext(); var service = Service(db);
        await service.ProcessExpiredOrdersAsync(); await service.ProcessExpiredOrdersAsync();
        Assert.Equal(OrderStatus.Cancelled, data.Query(x => x.Orders.Single(o => o.OrderId == id).OrderStatus));
        Assert.Equal(before + 1, data.Query(x => x.ProductVariants.Sum(v => v.StockQuantity)));
    }

    private static Dictionary<string, string> QueryStringFields(string url) => Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(url).Query)
        .ToDictionary(x => x.Key, x => x.Value.ToString());

    private sealed class QueryHandler(Func<Dictionary<string, string>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(response()), Encoding.UTF8, "application/json") });
    }

    private sealed class ForbiddenHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
    }

    public void Dispose() => data.Dispose();
}
