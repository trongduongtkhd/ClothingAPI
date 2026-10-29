using System.Net;
using System.Net.Http.Json;
using ClothingAPI.Helpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClothingAPI.IntegrationTests;

public class VnpayIntegrationTests
{
    [Fact]
    public async Task AnonymousIpnReturnsExactGatewayEnvelopeAndRejectsDuplicateParameters()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/payments/vnpay/ipn?vnp_TxnRef=a&vnp_TxnRef=b");
        var json = await TestHelpers.ReadJsonAsync(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("97", json.GetProperty("RspCode").GetString());
        Assert.True(json.TryGetProperty("Message", out _));
        Assert.False(json.TryGetProperty("success", out _));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/orders/1/vnpay/payment-url", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/admin/orders/1/vnpay/reconcile", new { })).StatusCode);
    }

    [Fact]
    public async Task AuthenticatedCheckoutAndSignedIpnUpdateRealApiWithoutFrontendConfirmation()
    {
        const string secret = "integration-vnpay-test-only-secret";
        using var factory = new CustomWebApplicationFactory();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.PostConfigure<VnpaySettings>(settings =>
            {
                settings.Enabled = true; settings.TmnCode = "TESTONLY"; settings.HashSecret = secret;
                settings.PublicBaseUrl = "https://shop.example.test"; settings.WorkerEnabled = false;
            })));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var user = await TestHelpers.RegisterAndLoginCustomerAsync(client,"vnpay");
        TestHelpers.Authorize(client,user.AccessToken);
        client.DefaultRequestHeaders.Add("Origin", "http://localhost:4200");
        var address = await TestHelpers.CreateAddressAsync(client);
        int variantId;
        await using (var db = factory.CreateDbContext()) variantId = await db.ProductVariants.Where(x => x.IsActive && x.StockQuantity > 0).Select(x => x.VariantId).FirstAsync();
        await TestHelpers.AddToCartAsync(client,variantId,1);
        var orderResponse = await client.PostAsJsonAsync("/api/orders",new { addressId = address, paymentMethod = "VnPay" });
        var orderJson = await TestHelpers.ReadJsonAsync(orderResponse);
        Assert.Equal(HttpStatusCode.Created,orderResponse.StatusCode);
        var order = orderJson.GetProperty("data");
        var id = order.GetProperty("orderId").GetInt32();
        var amount = order.GetProperty("totalAmount").GetDecimal();
        var start = await client.PostAsJsonAsync($"/api/orders/{id}/vnpay/payment-url",new { });
        Assert.Equal(HttpStatusCode.OK,start.StatusCode);
        var data = (await TestHelpers.ReadJsonAsync(start)).GetProperty("data");
        Assert.StartsWith("https://sandbox.vnpayment.vn/",data.GetProperty("paymentUrl").GetString());
        var fields = new Dictionary<string,string>
        {
            ["vnp_TmnCode"] = "TESTONLY", ["vnp_TxnRef"] = data.GetProperty("txnRef").GetString()!,
            ["vnp_Amount"] = VnpayProtocol.Amount(amount), ["vnp_ResponseCode"] = "00", ["vnp_TransactionStatus"] = "00",
            ["vnp_TransactionNo"] = "123456789", ["vnp_PayDate"] = VnpayProtocol.Date(DateTime.UtcNow)
        };
        var query = VnpayProtocol.QueryString(fields);
        query += "&vnp_SecureHash="+VnpayProtocol.Hash(secret,query);
        client.DefaultRequestHeaders.Authorization = null; // server-to-server callbacks have no customer JWT
        // The customer's browser return must settle the payment even if merchant IPN is not registered.
        var returned = await client.GetAsync("/api/payments/vnpay/return?"+query);
        Assert.Equal(HttpStatusCode.Redirect,returned.StatusCode);
        Assert.Equal($"http://localhost:4200/checkout/vnpay-result?orderId={id}",returned.Headers.Location!.ToString());
        var duplicate = await client.GetAsync("/api/payments/vnpay/ipn?"+query);
        Assert.Equal("02",(await TestHelpers.ReadJsonAsync(duplicate)).GetProperty("RspCode").GetString());
        TestHelpers.Authorize(client,user.AccessToken);
        var result = await client.GetAsync($"/api/orders/{id}/vnpay/status");
        var status = (await TestHelpers.ReadJsonAsync(result)).GetProperty("data");
        Assert.Equal("Paid",status.GetProperty("paymentStatus").GetString());
        Assert.False(status.GetProperty("canPay").GetBoolean());
        Assert.False(status.GetProperty("canCancel").GetBoolean());
        var other = await TestHelpers.RegisterAndLoginCustomerAsync(client,"other-vnpay");
        TestHelpers.Authorize(client,other.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/orders/{id}/vnpay/status")).StatusCode);
    }
}
