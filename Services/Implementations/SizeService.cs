using ClothingAPI.Data;
using ClothingAPI.DTOs.Sizes;
using ClothingAPI.Exceptions;
using ClothingAPI.Services.Interfaces;
using ClothingAPI.Models;
using Microsoft.EntityFrameworkCore;
namespace ClothingAPI.Services.Implementations
{
    public class SizeService : ISizeService
    {
        private readonly AppDbContext _context;

        public SizeService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<SizeDto>> GetAllAsync()
        {
            var sizes = await _context.Sizes
                 .AsNoTracking()
                .OrderBy(x => x.DisplayOrder)
                .ThenBy(x => x.SizeName)
                .ToListAsync();
            return sizes.Select(MapToDto).ToList();

        }

        public async Task<SizeDto> GetByIdAsync(int sizeId)
        {
            var size = await _context.Sizes
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.SizeId == sizeId);

            if (size is null)
            {
                throw new NotFoundException("Không tìm thấy size.");
            }

            return MapToDto(size);
        }

        public async Task<SizeDto> CreateAsync(UpsertSizeDto dto)
        {
            var sizeName = dto.SizeName.Trim().ToUpper();

            var exists = await _context.Sizes
                .AnyAsync(x => x.SizeName.ToUpper() == sizeName);

            if (exists)
            {
                throw new BadRequestException("Tên size đã tồn tại.");
            }

            var size = new Size
            {
                SizeName = sizeName,
                DisplayOrder = dto.DisplayOrder
            };

            _context.Sizes.Add(size);
            await _context.SaveChangesAsync();

            return MapToDto(size);
        }

        public async Task<SizeDto> UpdateAsync(int sizeId, UpsertSizeDto dto)
        {
            var size = await _context.Sizes
                .FirstOrDefaultAsync(x => x.SizeId == sizeId);

            if (size is null)
            {
                throw new NotFoundException("Không tìm thấy size.");
            }

            var sizeName = dto.SizeName.Trim().ToUpper();

            var exists = await _context.Sizes.AnyAsync(x =>
                x.SizeName.ToUpper() == sizeName &&
                x.SizeId != sizeId);

            if (exists)
            {
                throw new BadRequestException("Tên size đã tồn tại.");
            }

            size.SizeName = sizeName;
            size.DisplayOrder = dto.DisplayOrder;

            await _context.SaveChangesAsync();

            return MapToDto(size);
        }

        public async Task DeleteAsync(int sizeId)
        {
            var size = await _context.Sizes
                .FirstOrDefaultAsync(x => x.SizeId == sizeId);

            if (size is null)
            {
                throw new NotFoundException("Không tìm thấy size.");
            }

            _context.Sizes.Remove(size);
            await _context.SaveChangesAsync();
        }

        private static SizeDto MapToDto(Size size)
        {
            return new SizeDto
            {
                SizeId = size.SizeId,
                SizeName = size.SizeName,
                DisplayOrder = size.DisplayOrder
            };
        }
    }
}
