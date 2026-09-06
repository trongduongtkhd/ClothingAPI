using ClothingAPI.Data;
using ClothingAPI.DTOs.Users;
using ClothingAPI.Exceptions;
using ClothingAPI.Helpers;
using ClothingAPI.Models;
using ClothingAPI.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClothingAPI.Services.Implementations;

public class AdminUserService : IAdminUserService
{
    private readonly AppDbContext _context;

    public AdminUserService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<AdminUserDto>> GetAllAsync(AdminUserQueryDto query)

    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 10 : Math.Min(query.PageSize, 100);

        var users = _context.Users
            .AsNoTracking()
            .Include(x => x.UserRoles)
                .ThenInclude(x => x.Role)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.Trim().ToLower();

            users = users.Where(x =>
                x.FullName.ToLower().Contains(keyword) ||
                x.Email.ToLower().Contains(keyword) ||
                (x.PhoneNumber != null &&
                 x.PhoneNumber.ToLower().Contains(keyword)));
        }

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            var roleName = query.Role.Trim().ToLower();

            users = users.Where(x => x.UserRoles.Any(userRole => userRole.Role.RoleName.ToLower() == roleName));

        }

        if (query.IsActive.HasValue)
        {
            users = users.Where(x => x.IsActive == query.IsActive.Value);

        }

        var totalItems = await users.CountAsync();

        var userList = await users
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<AdminUserDto>
        {
            Items = userList.Select(MapToDto).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize)
        };
    }

    public async Task<AdminUserDto> GetByIdAsync(int userId)
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

        return MapToDto(user);
    }

    public async Task UpdateStatusAsync(int currentAdminUserId, int targetUserId, bool isActive)

    {
        if (currentAdminUserId == targetUserId)
        {
            throw new BadRequestException(
                "Bạn không thể tự khóa tài khoản của chính mình."
            );
        }

        var user = await _context.Users.FirstOrDefaultAsync(x => x.UserId == targetUserId);

        if (user is null)
        {
            throw new NotFoundException("Không tìm thấy người dùng.");
        }

        user.IsActive = isActive;
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
    }

    private static AdminUserDto MapToDto(User user)
    {
        return new AdminUserDto
        {
            UserId = user.UserId,
            FullName = user.FullName,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            AvatarUrl = user.AvatarUrl,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            Roles = user.UserRoles.Select(x => x.Role.RoleName).ToList()

        };
    }
}