using ClothingAPI.DTOs.Auth;

namespace ClothingAPI.Services.Interfaces
{
    public interface IRefreshTokenService
    {
        // Cấp token cho một phiên đăng nhập mới (family mới) – dùng khi đăng nhập / đăng ký.
        Task<IssuedRefreshToken> IssueAsync(int userId, string? ip, string? userAgent);

        // Xoay vòng: thu hồi token hiện tại, cấp token mới cùng family. Không throw, trả về trạng thái.
        Task<RefreshRotationResult> RotateAsync(string? rawToken, string? ip, string? userAgent);

        // Thu hồi phiên của thiết bị đang dùng token này (đăng xuất).
        Task RevokeFamilyAsync(string? rawToken, string reason);

        // Thu hồi mọi phiên của user (đổi / đặt lại mật khẩu, admin khóa tài khoản).
        Task RevokeAllForUserAsync(int userId, string reason);
    }
}
