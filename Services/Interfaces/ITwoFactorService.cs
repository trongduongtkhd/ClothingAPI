using ClothingAPI.DTOs.Auth;
using ClothingAPI.DTOs.Users;

namespace ClothingAPI.Services.Interfaces
{
    public interface ITwoFactorService
    {
        // Sinh secret mới (lưu đã mã hóa, chưa bật) và trả QR/khóa nhập tay. Từ chối nếu 2FA đã bật.
        Task<TwoFactorSetupDto> GetSetupAsync(int userId);

        // Xác nhận mã đầu tiên => bật 2FA, sinh mã khôi phục (chỉ trả một lần), thu hồi mọi phiên cũ.
        Task<EnableTwoFactorResultDto> EnableAsync(int userId, string code, string? ip);

        // Tắt 2FA: bắt buộc mật khẩu + (mã hiện tại hoặc mã khôi phục). Sai thì tính vào khóa tài khoản.
        Task DisableAsync(int userId, DisableTwoFactorDto dto, string? ip);

        // Bước 1 đăng nhập (mật khẩu đúng, user bật 2FA): tạo challenge đục, chưa cấp token.
        Task<TwoFactorChallengeDto> CreateLoginChallengeAsync(int userId, string? ip);

        // Bước 2 đăng nhập. Không throw cho lỗi dự kiến, trả về trạng thái.
        Task<TwoFactorLoginResult> VerifyLoginCodeAsync(string? rawChallenge, string? code, string? ip);

        Task<TwoFactorLoginResult> VerifyLoginRecoveryCodeAsync(string? rawChallenge, string? recoveryCode, string? ip);
    }
}
