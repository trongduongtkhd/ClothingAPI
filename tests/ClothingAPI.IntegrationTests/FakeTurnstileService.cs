using ClothingAPI.DTOs.Auth;
using ClothingAPI.Services.Interfaces;

namespace ClothingAPI.IntegrationTests;

/// Thay Cloudflare Turnstile trong integration test (không gọi mạng). Kết quả quyết định theo GIÁ TRỊ token
/// nên không có trạng thái dùng chung giữa các test.
public sealed class FakeTurnstileService : ITurnstileService
{
    public const string PassToken = "test-pass";
    public const string InvalidToken = "test-invalid";
    public const string UnavailableToken = "test-unavailable";

    public Task<TurnstileVerifyResult> VerifyAsync(string? token, CancellationToken cancellationToken = default) =>
        Task.FromResult(token switch
        {
            PassToken => new TurnstileVerifyResult(TurnstileVerifyStatus.Valid, [], string.Empty),
            UnavailableToken => new TurnstileVerifyResult(
                TurnstileVerifyStatus.Unavailable, [], "Hệ thống xác minh đang bận, vui lòng thử lại sau ít phút.", "Giả lập mất mạng"),
            _ => new TurnstileVerifyResult(
                TurnstileVerifyStatus.Invalid, ["invalid-input-response"], "Xác minh CAPTCHA không hợp lệ, vui lòng thử lại.")
        });
}
