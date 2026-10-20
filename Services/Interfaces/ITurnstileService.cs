using ClothingAPI.DTOs.Auth;

namespace ClothingAPI.Services.Interfaces
{
    public interface ITurnstileService
    {
        // Xác minh token CAPTCHA với Cloudflare. Không throw cho lỗi dự kiến (kể cả lỗi mạng), trả về trạng thái.
        Task<TurnstileVerifyResult> VerifyAsync(string? token, CancellationToken cancellationToken = default);
    }
}
