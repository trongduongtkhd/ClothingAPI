using ClothingAPI.Exceptions;
using System.Security.Claims;

namespace ClothingAPI.Helpers
{
    public static class ClaimsPrincipalExtensions
    {
        public static int GetUserId(this ClaimsPrincipal user)
        {
            var userIdText = user.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!int.TryParse(userIdText, out var userId))
            {
                throw new UnauthorizedException("Token không hợp lệ.");
            }

            return userId;
        }
    }
}
