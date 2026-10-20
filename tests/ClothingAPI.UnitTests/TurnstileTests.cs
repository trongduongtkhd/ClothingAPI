using System.Net;
using System.Text;
using ClothingAPI.DTOs.Auth;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Implementations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit.Abstractions;

namespace ClothingAPI.UnitTests;

/// TurnstileService: phân loại phản hồi của Cloudflare thành 3 trạng thái Valid / Invalid / Unavailable.
/// Không gọi mạng thật: HttpMessageHandler được giả lập cho từng kịch bản.
public class TurnstileTests : UnitTestBase
{
    private const string Token = "token-turnstile-gia-lap-0123456789";
    private const string SecretKey = "secret-key-gia-lap-khong-duoc-xuat-hien-trong-log";

    private readonly ListLogger<TurnstileService> _logger = new();

    public TurnstileTests(ITestOutputHelper output) : base(output)
    {
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

        public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) => _send = send;

        public int CallCount { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return await _send(request, cancellationToken);
        }
    }

    private static FakeHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));

    private TurnstileService NewService(FakeHandler handler, bool enabled = true, TimeSpan? timeout = null)
    {
        var client = new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromSeconds(5) };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(TurnstileService.HttpClientName)).Returns(client);

        return new TurnstileService(
            factory.Object,
            Options.Create(new TurnstileSettings { SecretKey = SecretKey, Enabled = enabled }),
            _logger);
    }

    private void Report(TurnstileVerifyResult result, FakeHandler handler)
    {
        Actual($"Status = {result.Status}; ErrorCodes = [{string.Join(", ", result.ErrorCodes)}]; " +
               $"Message = \"{result.Message}\"; FailureDetail = {result.FailureDetail ?? "(null)"}; số lần gọi Cloudflare = {handler.CallCount}");
    }

    private void AssertNoSecretsLogged()
    {
        Assert.All(_logger.Entries, entry =>
        {
            Assert.DoesNotContain(Token, entry.Message);
            Assert.DoesNotContain(SecretKey, entry.Message);
        });
    }

    [Fact]
    public async Task UT123_Turnstile_SuccessTrue_HopLe()
    {
        var handler = Json("""{"success":true,"error-codes":[],"hostname":"localhost"}""");
        Step1("Cloudflare trả HTTP 200, success = true");

        var result = await NewService(handler).VerifyAsync(Token);
        Report(result, handler);

        Assert.Equal(TurnstileVerifyStatus.Valid, result.Status);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task UT124_Turnstile_SuccessFalse_KhongHopLe_KemErrorCodes()
    {
        var handler = Json("""{"success":false,"error-codes":["invalid-input-response"]}""");
        Step1("Cloudflare trả HTTP 200, success = false, error-codes = [invalid-input-response]");

        var result = await NewService(handler).VerifyAsync(Token);
        Report(result, handler);

        Assert.Equal(TurnstileVerifyStatus.Invalid, result.Status);
        Assert.Equal(new[] { "invalid-input-response" }, result.ErrorCodes);
        Assert.Equal("Xác minh CAPTCHA không hợp lệ, vui lòng thử lại.", result.Message);
    }

    [Fact]
    public async Task UT125_Turnstile_TimeoutOrDuplicate_ThongBaoHetHan()
    {
        var handler = Json("""{"success":false,"error-codes":["timeout-or-duplicate"]}""");
        Step1("Gửi lại token đã dùng / quá 5 phút: Cloudflare trả error-codes = [timeout-or-duplicate]");

        var result = await NewService(handler).VerifyAsync(Token);
        Report(result, handler);

        Assert.Equal(TurnstileVerifyStatus.Invalid, result.Status);
        Assert.Equal("Phiên xác minh đã hết hạn, vui lòng thử lại.", result.Message);
    }

    [Fact]
    public async Task UT126_Turnstile_LoiMang_KhongGoiDuoc()
    {
        var handler = new FakeHandler((_, _) => throw new HttpRequestException("Không phân giải được tên miền", null, HttpStatusCode.ServiceUnavailable));
        Step1("HttpMessageHandler ném HttpRequestException (mất mạng / DNS lỗi)");

        var result = await NewService(handler).VerifyAsync(Token);
        Report(result, handler);

        Assert.Equal(TurnstileVerifyStatus.Unavailable, result.Status);
        Assert.StartsWith("HttpRequestException", result.FailureDetail);
    }

    [Fact]
    public async Task UT127_Turnstile_Timeout_KhongGoiDuoc()
    {
        var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        Step1("Cloudflare không phản hồi; HttpClient.Timeout = 100ms");

        var result = await NewService(handler, timeout: TimeSpan.FromMilliseconds(100)).VerifyAsync(Token);
        Report(result, handler);

        Assert.Equal(TurnstileVerifyStatus.Unavailable, result.Status);
        Assert.Equal("Timeout", result.FailureDetail);
    }

    [Fact]
    public async Task UT128_Turnstile_Http503_KhongGoiDuoc()
    {
        var handler = Json("<html>Service Unavailable</html>", HttpStatusCode.ServiceUnavailable);
        Step1("Cloudflare trả HTTP 503 (body HTML)");

        var result = await NewService(handler).VerifyAsync(Token);
        Report(result, handler);

        Assert.Equal(TurnstileVerifyStatus.Unavailable, result.Status);
        Assert.Equal("HTTP 503", result.FailureDetail);
    }

    [Fact]
    public async Task UT129_Turnstile_TatCaptcha_HopLe_KhongGoiCloudflare()
    {
        var handler = Json("""{"success":false,"error-codes":["invalid-input-response"]}""");
        Step1("TurnstileSettings.Enabled = false");

        var result = await NewService(handler, enabled: false).VerifyAsync(Token);
        Report(result, handler);

        Assert.Equal(TurnstileVerifyStatus.Valid, result.Status);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task UT130_Turnstile_TokenRong_KhongHopLe_KhongGoiCloudflare()
    {
        var handler = Json("""{"success":true,"error-codes":[]}""");
        Step1("Token rỗng (chỉ có khoảng trắng)");

        var result = await NewService(handler).VerifyAsync("   ");
        Report(result, handler);

        Assert.Equal(TurnstileVerifyStatus.Invalid, result.Status);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task UT131_Turnstile_KhongGuiRemoteIp()
    {
        var handler = Json("""{"success":true,"error-codes":[]}""");
        Step1("Xác minh một token hợp lệ, kiểm tra body form gửi lên Cloudflare");

        await NewService(handler).VerifyAsync(Token);
        var fields = handler.LastBody!.Split('&').Select(pair => pair.Split('=')[0]).ToList();
        Actual($"Các trường trong body gửi đi: {string.Join(", ", fields)}");

        Assert.Equal(new[] { "secret", "response" }, fields);
    }

    [Fact]
    public async Task UT132_Turnstile_SecretSai_KhongHopLe_LogErrorKhongLoSecret()
    {
        var handler = Json("""{"success":false,"error-codes":["invalid-input-secret"]}""");
        Step1("Secret key cấu hình sai: Cloudflare trả error-codes = [invalid-input-secret]");

        var result = await NewService(handler).VerifyAsync(Token);
        Report(result, handler);

        // Lỗi cấu hình KHÔNG được xếp vào Unavailable, nếu không login (fail-open) sẽ âm thầm bỏ qua CAPTCHA.
        Assert.Equal(TurnstileVerifyStatus.Invalid, result.Status);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Error);
        AssertNoSecretsLogged();
    }

    [Fact]
    public async Task UT137_Turnstile_SecretSai_Http400_VanKhongHopLe()
    {
        // Phản hồi THẬT của Cloudflare khi secret sai (đã kiểm chứng bằng curl): HTTP 400, không phải 200.
        var handler = Json("""{"error-codes":["invalid-input-secret"],"success":false,"messages":[]}""", HttpStatusCode.BadRequest);
        Step1("Secret key sai: Cloudflare trả HTTP 400, error-codes = [invalid-input-secret]");

        var result = await NewService(handler).VerifyAsync(Token);
        Report(result, handler);

        // Nếu coi mọi status khác 2xx là Unavailable thì login (fail-open) sẽ âm thầm chạy không có CAPTCHA.
        Assert.Equal(TurnstileVerifyStatus.Invalid, result.Status);
        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Error);
        AssertNoSecretsLogged();
    }
}
