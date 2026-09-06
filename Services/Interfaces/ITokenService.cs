using ClothingAPI.Models;

namespace ClothingAPI.Services.Interfaces
{
    public interface ITokenService
    {
        string CreateToken(User user, IEnumerable<string> roles);
    }
}
