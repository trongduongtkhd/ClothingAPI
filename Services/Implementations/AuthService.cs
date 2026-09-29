using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ClothingAPI.Data;
using ClothingAPI.DTOs.Auth;
using ClothingAPI.Exceptions;
using ClothingAPI.Helpers;
using ClothingAPI.Models;
using ClothingAPI.Services.Interfaces;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace ClothingAPI.Services.Implementations
{
    public class AuthService : IAuthService
    {
        private readonly AppDbContext _context;
        private readonly ITokenService _tokenService;
        private readonly IEmailService _emailService;
        private readonly FrontendSettings _frontendSettings;
        private readonly ILogger<AuthService> _logger;

        public AuthService(
            AppDbContext context,
            ITokenService tokenService,
            IEmailService emailService,
            IOptions<FrontendSettings> frontendSettings,
            ILogger<AuthService> logger)
        {
            _context = context;
            _tokenService = tokenService;
            _emailService = emailService;
            _frontendSettings = frontendSettings.Value;
            _logger = logger;
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

        private const int MaxFailedLoginAttempts = 5;
        private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

        public async Task<AuthResponseDto> LoginAsync(LoginDto loginDto)
        {
            var email = loginDto.Email.Trim().ToLower();

            var user = await _context.Users
                .Include(x => x.UserRoles)
                .ThenInclude(x => x.Role)
                .FirstOrDefaultAsync(x => x.Email == email);

            if (user is not null &&
                user.LockoutEndUtc.HasValue &&
                user.LockoutEndUtc.Value > DateTime.UtcNow)
            {
                var remainingMinutes = (int)Math.Ceiling(
                    (user.LockoutEndUtc.Value - DateTime.UtcNow).TotalMinutes);

                _logger.LogWarning(
                    "Từ chối đăng nhập: tài khoản {Email} đang bị khóa đến {LockoutEnd} UTC (còn {RemainingMinutes} phút).",
                    email, user.LockoutEndUtc.Value, remainingMinutes
                );

                throw new UnauthorizedException(
                    $"Tài khoản tạm khóa do đăng nhập sai quá nhiều lần. Vui lòng thử lại sau {remainingMinutes} phút."
                );
            }

            if (user is null || !BCrypt.Net.BCrypt.Verify(loginDto.Password, user.PasswordHash))
            {
                if (user is not null)
                {
                    await RegisterFailedLoginAsync(user);
                }

                throw new UnauthorizedException("Email hoặc mật khẩu không đúng.");
            }

            if (!user.IsActive)
            {
                throw new UnauthorizedException("Tài khoản của bạn đã bị khóa.");
            }

            if (user.FailedLoginAttempts > 0 || user.LockoutEndUtc.HasValue)
            {
                _logger.LogInformation(
                    "Đăng nhập thành công cho {Email}, đã reset bộ đếm đăng nhập sai.",
                    user.Email
                );

                user.FailedLoginAttempts = 0;
                user.LockoutEndUtc = null;
                await _context.SaveChangesAsync();
            }

            var roles = user.UserRoles.Select(x => x.Role.RoleName).ToList();

            return CreateAuthResponse(user, roles);
        }

        private async Task RegisterFailedLoginAsync(User user)
        {
            user.FailedLoginAttempts += 1;

            _logger.LogWarning(
                "Đăng nhập sai lần {Attempt}/{MaxAttempts} cho tài khoản {Email}.",
                user.FailedLoginAttempts, MaxFailedLoginAttempts, user.Email
            );

            if (user.FailedLoginAttempts >= MaxFailedLoginAttempts)
            {
                user.LockoutEndUtc = DateTime.UtcNow.Add(LockoutDuration);
                user.FailedLoginAttempts = 0;

                _logger.LogWarning(
                    "Tài khoản {Email} bị khóa đến {LockoutEnd} UTC do đăng nhập sai {MaxAttempts} lần liên tiếp.",
                    user.Email, user.LockoutEndUtc.Value, MaxFailedLoginAttempts
                );
            }

            await _context.SaveChangesAsync();
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

        public async Task ForgotPasswordAsync(ForgotPasswordDto forgotPasswordDto)
        {
            var email = forgotPasswordDto.Email.Trim().ToLower();

            var user = await _context.Users.FirstOrDefaultAsync(x => x.Email == email);

            if (user is null || !user.IsActive)
            {
                return;
            }

            var token = GenerateResetToken();

            user.ResetPasswordToken = token;
            user.ResetPasswordTokenExpiry = DateTime.UtcNow.AddMinutes(30);
            await _context.SaveChangesAsync();

            var resetLink = $"{_frontendSettings.BaseUrl}/auth/reset-password?token={token}";

            var htmlBody = $"""
                <p>Xin chào {user.FullName},</p>
                <p>Chúng tôi nhận được yêu cầu đặt lại mật khẩu cho tài khoản Clothing Store của bạn.</p>
                <p>Vui lòng bấm vào liên kết bên dưới để đặt lại mật khẩu. Liên kết có hiệu lực trong 30 phút:</p>
                <p><a href="{resetLink}">Đặt lại mật khẩu</a></p>
                <p>Nếu bạn không yêu cầu điều này, vui lòng bỏ qua email này.</p>
                """;

            try
            {
                await _emailService.SendAsync(user.Email, "Đặt lại mật khẩu - Clothing Store", htmlBody);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Gửi email đặt lại mật khẩu thất bại cho {Email}", user.Email);
            }
        }

        public async Task ResetPasswordAsync(ResetPasswordDto resetPasswordDto)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(x => x.ResetPasswordToken == resetPasswordDto.Token);

            if (user is null || user.ResetPasswordTokenExpiry is null || user.ResetPasswordTokenExpiry < DateTime.UtcNow)
            {
                throw new BadRequestException("Token không hợp lệ hoặc đã hết hạn.");
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(resetPasswordDto.NewPassword);
            user.ResetPasswordToken = null;
            user.ResetPasswordTokenExpiry = null;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
        }

        private static string GenerateResetToken()
        {
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
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
