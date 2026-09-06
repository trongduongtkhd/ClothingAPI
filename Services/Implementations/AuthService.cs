using Microsoft.EntityFrameworkCore;
using ClothingAPI.Data;
using ClothingAPI.DTOs.Auth;
using ClothingAPI.Exceptions;
using ClothingAPI.Models;
using ClothingAPI.Services.Interfaces;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ClothingAPI.Services.Implementations
{
    public class AuthService : IAuthService
    {
        private readonly AppDbContext _context;
        private readonly ITokenService _tokenService;

        public AuthService(AppDbContext context, ITokenService tokenService)
        {
            _context = context;
            _tokenService = tokenService;
        }

        public async Task<AuthResponseDto> RegisterAsync(RegisterDto registerDto)
        {
            var email = registerDto.Email.Trim().ToLower();

            var emailExists = await _context.Users.AnyAsync(x => x.Email == email);

            if (emailExists)
            {
                throw new BadRequestException("Email này đã được sử dụng.");
            }

            var customerRole = await _context.Roles.FirstOrDefaultAsync(x => x.RoleName == "Customer");

            if (customerRole is null)
            {
                throw new InvalidOperationException("Không tìm thấy role Customer trong database.");
            }

            var user = new User
            {
                FullName = registerDto.FullName.Trim(),
                Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(registerDto.Password),
                PhoneNumber = string.IsNullOrWhiteSpace(registerDto.PhoneNumber) ? null : registerDto.PhoneNumber.Trim(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            user.UserRoles.Add(new UserRole
            {
                RoleId = customerRole.RoleId
            });

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return CreateAuthResponse(user, ["Customer"]);
        }

        public async Task<AuthResponseDto> LoginAsync(LoginDto loginDto)
        {
            var email = loginDto.Email.Trim().ToLower();

            var user = await _context.Users
                .Include(x => x.UserRoles)
                .ThenInclude(x => x.Role)
                .FirstOrDefaultAsync(x => x.Email == email);

            if (user is null || !BCrypt.Net.BCrypt.Verify(loginDto.Password, user.PasswordHash))
            {
                throw new UnauthorizedException("Email hoặc mật khẩu không đúng.");
            }

            if (!user.IsActive)
            {
                throw new UnauthorizedException("Tài khoản của bạn đã bị khóa.");
            }

            var roles = user.UserRoles.Select(x => x.Role.RoleName).ToList();

            return CreateAuthResponse(user, roles);
        }

        public async Task<CurrentUserDto> GetCurrentUserAsync(int userId)
        {
            var user = await _context.Users
                .Include(x => x.UserRoles)
                .ThenInclude(x => x.Role)
                .FirstOrDefaultAsync(x => x.UserId == userId);

            if (user is null)
            {
                throw new UnauthorizedException("Không tìm thấy người dùng.");
            }

            return new CurrentUserDto
            {
                UserId = user.UserId,
                FullName = user.FullName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                AvatarUrl = user.AvatarUrl,
                Roles = user.UserRoles.Select(x => x.Role.RoleName).ToList()
            };
        }

        private AuthResponseDto CreateAuthResponse(User user, List<string> roles)
        {
            var expiresAt = DateTime.UtcNow.AddMinutes(120);

            var accessToken = _tokenService.CreateToken(user, roles);

            return new AuthResponseDto
            {
                AccessToken = accessToken,
                ExpiresAt = expiresAt,
                User = new CurrentUserDto
                {
                    UserId = user.UserId,
                    FullName = user.FullName,
                    Email = user.Email,
                    PhoneNumber = user.PhoneNumber,
                    AvatarUrl = user.AvatarUrl,
                    Roles = roles
                }
            };
        }
    }
}
