using ClothingAPI.Models;

namespace ClothingAPI.Services.Interfaces
{
    public interface ITokenService
    {
        // Trả về cả thời điểm hết hạn để AuthResponseDto.ExpiresAt luôn khớp claim "exp" của JWT.
        (string Token, DateTime ExpiresAt) CreateToken(User user, IEnumerable<string> roles);
    }
}
