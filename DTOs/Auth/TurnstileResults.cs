namespace ClothingAPI.DTOs.Auth
{
    public enum TurnstileVerifyStatus
    {
        // Cloudflare xác nhận token hợp lệ.
        Valid,

        // Cloudflare trả success = false (token sai, hết hạn, đã dùng...) hoặc token rỗng.
        Invalid,

        // Không gọi được Cloudflare (timeout, lỗi mạng, 5xx, internal-error): nơi gọi tự quyết định cho qua hay chặn.
        Unavailable
    }

    /// ErrorCodes là error-codes của Cloudflare, FailureDetail chỉ dùng để ghi log; không trường nào chứa token.
    public record TurnstileVerifyResult(
        TurnstileVerifyStatus Status,
        IReadOnlyList<string> ErrorCodes,
        string Message,
        string? FailureDetail = null)
    {
        public bool Succeeded => Status == TurnstileVerifyStatus.Valid;
    }
}
