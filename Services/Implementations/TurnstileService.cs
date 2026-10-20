using ClothingAPI.DTOs.Auth;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClothingAPI.Services.Implementations
{
    /// Xác minh token Cloudflare Turnstile qua siteverify. Phân biệt rõ 3 trạng thái: hợp lệ / không hợp lệ /
    /// không gọi được Cloudflare, để nơi gọi tự chọn cho qua (login) hay chặn (register).
    /// TUYỆT ĐỐI không ghi token hay secret key ra log.
    public class TurnstileService : ITurnstileService
    {
        public const string HttpClientName = "Turnstile";

        // Giới hạn độ dài token của Turnstile; dài hơn chắc chắn là rác, không cần gọi Cloudflare.
        private const int MaxTokenLength = 2048;

        private const string InvalidMessage = "Xác minh CAPTCHA không hợp lệ, vui lòng thử lại.";
        private const string ExpiredMessage = "Phiên xác minh đã hết hạn, vui lòng thử lại.";
        private const string MissingMessage = "Vui lòng hoàn tất xác minh CAPTCHA.";
        private const string UnavailableMessage = "Hệ thống xác minh đang bận, vui lòng thử lại sau ít phút.";
        private const string MisconfiguredMessage = "Hệ thống xác minh CAPTCHA đang gặp sự cố, vui lòng thử lại sau.";

        // Lỗi cấu hình phía mình (secret sai/thiếu): coi là KHÔNG hợp lệ để login không âm thầm fail-open.
        private static readonly string[] SecretErrorCodes = ["missing-input-secret", "invalid-input-secret"];

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly TurnstileSettings _settings;
        private readonly ILogger<TurnstileService> _logger;

        public TurnstileService(
            IHttpClientFactory httpClientFactory,
            IOptions<TurnstileSettings> settings,
            ILogger<TurnstileService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _settings = settings.Value;
            _logger = logger;
        }

        public async Task<TurnstileVerifyResult> VerifyAsync(string? token, CancellationToken cancellationToken = default)
        {
            if (!_settings.Enabled)
            {
                return new TurnstileVerifyResult(TurnstileVerifyStatus.Valid, [], string.Empty);
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                return new TurnstileVerifyResult(TurnstileVerifyStatus.Invalid, ["missing-input-response"], MissingMessage);
            }

            if (token.Length > MaxTokenLength)
            {
                return new TurnstileVerifyResult(TurnstileVerifyStatus.Invalid, ["invalid-input-response"], InvalidMessage);
            }

            // Không gửi remoteip: chạy sau nginx thì IP nhận được là IP container, gửi lên sẽ làm kết quả sai.
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["secret"] = _settings.SecretKey,
                ["response"] = token
            });

            try
            {
                var client = _httpClientFactory.CreateClient(HttpClientName);
                using var response = await client.PostAsync(_settings.VerifyUrl, content, cancellationToken);

                if ((int)response.StatusCode >= 500)
                {
                    return Unavailable($"HTTP {(int)response.StatusCode}");
                }

                SiteVerifyResponse? body;
                try
                {
                    body = await response.Content.ReadFromJsonAsync<SiteVerifyResponse>(cancellationToken);
                }
                catch (JsonException)
                {
                    body = null;
                }

                if (body is null)
                {
                    return Unavailable($"Phản hồi không đọc được (HTTP {(int)response.StatusCode})");
                }

                if (!response.IsSuccessStatusCode && body.ErrorCodes.Length == 0)
                {
                    return Unavailable($"HTTP {(int)response.StatusCode}");
                }

                return Classify(body);
            }
            catch (HttpRequestException ex)
            {
                // Chỉ ghi loại lỗi / mã lỗi socket, không ghi nội dung request.
                return Unavailable($"HttpRequestException ({ex.HttpRequestError})");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // HttpClient.Timeout hết hạn (không phải client tự ngắt kết nối).
                return Unavailable("Timeout");
            }
        }

        private TurnstileVerifyResult Classify(SiteVerifyResponse body)
        {
            var codes = body.ErrorCodes;

            if (body.Success)
            {
                return new TurnstileVerifyResult(TurnstileVerifyStatus.Valid, codes, string.Empty);
            }

            if (codes.Any(code => SecretErrorCodes.Contains(code)))
            {
                _logger.LogError(
                    "Cloudflare Turnstile từ chối secret key (error-codes: {ErrorCodes}). Kiểm tra lại TurnstileSettings:SecretKey.",
                    string.Join(", ", codes));

                return new TurnstileVerifyResult(TurnstileVerifyStatus.Invalid, codes, MisconfiguredMessage);
            }

            // internal-error: lỗi nội bộ phía Cloudflare, có thể thử lại => coi như không gọi được.
            if (codes.Length > 0 && codes.All(code => code == "internal-error"))
            {
                return new TurnstileVerifyResult(TurnstileVerifyStatus.Unavailable, codes, UnavailableMessage, "internal-error");
            }

            var message = codes.Contains("timeout-or-duplicate") ? ExpiredMessage
                : codes.Contains("missing-input-response") ? MissingMessage
                : InvalidMessage;

            return new TurnstileVerifyResult(TurnstileVerifyStatus.Invalid, codes, message);
        }

        private static TurnstileVerifyResult Unavailable(string detail) =>
            new(TurnstileVerifyStatus.Unavailable, [], UnavailableMessage, detail);

        private sealed class SiteVerifyResponse
        {
            [JsonPropertyName("success")]
            public bool Success { get; set; }

            [JsonPropertyName("error-codes")]
            public string[] ErrorCodes { get; set; } = [];
        }
    }
}
