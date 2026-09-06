using ClothingAPI.Data;
using ClothingAPI.DTOs.Auth;
using ClothingAPI.DTOs.Users;
using ClothingAPI.Exceptions;
using ClothingAPI.Models;
using ClothingAPI.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClothingAPI.Services.Implementations;

public class UserProfileService : IUserProfileService
{
    private readonly AppDbContext _context;
    public UserProfileService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<CurrentUserDto> GetProfileAsync(int userId)
    {
        var user = await GetUserWithRolesAsync(userId);

        return MapToCurrentUserDto(user);
    }

    public async Task<CurrentUserDto> UpdateProfileAsync(int userId, UpdateProfileDto dto)
    {
        var user = await _context.Users
            .Include(x => x.UserRoles)
                .ThenInclude(x => x.Role)
            .FirstOrDefaultAsync(x => x.UserId == userId);

        if (user is null)
        {
            throw new NotFoundException("Không tìm thấy người dùng.");
        }

        user.FullName = dto.FullName.Trim();
        user.PhoneNumber = NormalizeNullableText(dto.PhoneNumber);
        user.AvatarUrl = NormalizeNullableText(dto.AvatarUrl);
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return MapToCurrentUserDto(user);
    }

    public async Task ChangePasswordAsync(int userId, ChangePasswordDto dto)
    {
        var user = await _context.Users.FirstOrDefaultAsync(x => x.UserId == userId);

        if (user is null)
        {
            throw new NotFoundException("Không tìm thấy người dùng.");
        }

        var isCorrectPassword = BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash);

        if (!isCorrectPassword)
        {
            throw new BadRequestException("Mật khẩu hiện tại không đúng.");
        }

        if (dto.CurrentPassword == dto.NewPassword)
        {
            throw new BadRequestException("Mật khẩu mới không được trùng mật khẩu hiện tại.");

        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
    }

    private async Task<User> GetUserWithRolesAsync(int userId)
    {
        var user = await _context.Users
            .AsNoTracking()
            .Include(x => x.UserRoles)
                .ThenInclude(x => x.Role)
            .FirstOrDefaultAsync(x => x.UserId == userId);

        if (user is null)
        {
            throw new NotFoundException("Không tìm thấy người dùng.");
        }
        return user;
    }

    private static CurrentUserDto MapToCurrentUserDto(User user)
    {
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

    private static string? NormalizeNullableText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}