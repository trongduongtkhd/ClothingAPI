using ClothingAPI.Data;
using ClothingAPI.DTOs.Addresses;
using ClothingAPI.Exceptions;
using ClothingAPI.Models;
using ClothingAPI.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClothingAPI.Services.Implementations;

public class AddressService : IAddressService
{
    private readonly AppDbContext _context;

    public AddressService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<AddressDto>> GetMyAddressesAsync(int userId)
    {
        var addresses = await _context.Addresses
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.IsDefault)
            .ThenByDescending(x => x.CreatedAt)
            .ToListAsync();

        return addresses.Select(MapToDto).ToList();
    }

    public async Task<AddressDto> CreateAsync(int userId, UpsertAddressDto dto)

    {
        var hasAnyAddress = await _context.Addresses.AnyAsync(x => x.UserId == userId);

        // Địa chỉ đầu tiên luôn được đặt mặc định.
        var shouldBeDefault = dto.IsDefault || !hasAnyAddress;

        if (shouldBeDefault)
        {
            await ClearDefaultAddressAsync(userId);
        }

        var address = new Address
        {
            UserId = userId,
            ReceiverName = dto.ReceiverName.Trim(),
            ReceiverPhone = dto.ReceiverPhone.Trim(),
            AddressDetail = dto.AddressDetail.Trim(),
            Ward = NormalizeNullableText(dto.Ward),
            District = NormalizeNullableText(dto.District),
            Province = dto.Province.Trim(),
            IsDefault = shouldBeDefault,
            CreatedAt = DateTime.UtcNow
        };

        _context.Addresses.Add(address);
        await _context.SaveChangesAsync();

        return MapToDto(address);
    }

    public async Task<AddressDto> UpdateAsync(int userId, int addressId, UpsertAddressDto dto)

    {
        var address = await GetAddressEntityAsync(userId, addressId);

        if (dto.IsDefault && !address.IsDefault)
        {
            await ClearDefaultAddressAsync(userId);
            address.IsDefault = true;
        }

        address.ReceiverName = dto.ReceiverName.Trim();
        address.ReceiverPhone = dto.ReceiverPhone.Trim();
        address.AddressDetail = dto.AddressDetail.Trim();
        address.Ward = NormalizeNullableText(dto.Ward);
        address.District = NormalizeNullableText(dto.District);
        address.Province = dto.Province.Trim();

        await _context.SaveChangesAsync();

        return MapToDto(address);
    }

    public async Task SetDefaultAsync(int userId, int addressId)
    {
        var targetAddress = await _context.Addresses
            .FirstOrDefaultAsync(x =>
                x.AddressId == addressId &&
                x.UserId == userId
            );

        if (targetAddress is null)
        {
            throw new NotFoundException("Không tìm thấy địa chỉ.");
        }

        if (targetAddress.IsDefault)
        {
            return;
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();

        // Bước 1: bỏ trạng thái mặc định của các địa chỉ cũ trước.
        await _context.Addresses
            .Where(x => x.UserId == userId && x.AddressId != addressId && x.IsDefault)

            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsDefault, false));

        // Bước 2: đặt địa chỉ được chọn thành mặc định.
        targetAddress.IsDefault = true;

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task DeleteAsync(int userId, int addressId)
    {
        var address = await GetAddressEntityAsync(userId, addressId);
        var wasDefault = address.IsDefault;

        await using var transaction = await _context.Database.BeginTransactionAsync();

        _context.Addresses.Remove(address);
        await _context.SaveChangesAsync();
        // Nếu xóa địa chỉ mặc định, tự chọn địa chỉ còn lại mới nhất.
        if (wasDefault)
        {
            var replacementAddress = await _context.Addresses
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();

            if (replacementAddress is not null)
            {
                replacementAddress.IsDefault = true;
                await _context.SaveChangesAsync();
            }
        }

        await transaction.CommitAsync();
    }

    private async Task<Address> GetAddressEntityAsync(int userId, int addressId)
    {
        var address = await _context.Addresses.FirstOrDefaultAsync(x => x.AddressId == addressId && x.UserId == userId);

        if (address is null)
        {
            throw new NotFoundException("Không tìm thấy địa chỉ.");
        }

        return address;
    }

    private async Task ClearDefaultAddressAsync(int userId)
    {
        var defaultAddresses = await _context.Addresses
            .Where(x => x.UserId == userId && x.IsDefault)
            .ToListAsync();

        foreach (var defaultAddress in defaultAddresses)
        {
            defaultAddress.IsDefault = false;
        }
    }

    private static AddressDto MapToDto(Address address)
    {
        return new AddressDto
        {
            AddressId = address.AddressId,
            ReceiverName = address.ReceiverName,
            ReceiverPhone = address.ReceiverPhone,
            AddressDetail = address.AddressDetail,
            Ward = address.Ward,
            District = address.District,
            Province = address.Province,
            IsDefault = address.IsDefault,
            CreatedAt = address.CreatedAt
        };
    }

    private static string? NormalizeNullableText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}