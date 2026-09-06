using ClothingAPI.Data;
using ClothingAPI.DTOs.Colors;
using ClothingAPI.Exceptions;
using ClothingAPI.Models;
using ClothingAPI.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
namespace ClothingAPI.Services.Implementations
{
    public class ColorService : IColorService
    {
        private readonly AppDbContext _context;

        public ColorService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<ColorDto>> GetAllAsync()
        {

            var colors = await _context.Colors
                .AsNoTracking()
                .OrderBy(x => x.ColorName)
                .ToListAsync();
            return colors.Select(MapToDto).ToList();    

        }

        public async Task<ColorDto> GetByIdAsync(int colorId)
        {
            var color = await _context.Colors
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.ColorId == colorId);

            if (color is null)
            {
                throw new NotFoundException("Không tìm thấy màu sắc.");
            }

            return MapToDto(color);
        }

        public async Task<ColorDto> CreateAsync(UpsertColorDto dto)
        {
            var colorName = dto.ColorName.Trim().ToLower();

            var exists = await _context.Colors
                .AnyAsync(x => x.ColorName.ToLower() == colorName);

            if (exists)
            {
                throw new BadRequestException("Tên màu đã tồn tại.");
            }

            var color = new Color
            {
                ColorName = dto.ColorName.Trim(),
                ColorCode = NormalizeNullableText(dto.ColorCode)
            };

            _context.Colors.Add(color);
            await _context.SaveChangesAsync();

            return MapToDto(color);
        }

        public async Task<ColorDto> UpdateAsync(int colorId, UpsertColorDto dto)
        {
            var color = await _context.Colors
                .FirstOrDefaultAsync(x => x.ColorId == colorId);

            if (color is null)
            {
                throw new NotFoundException("Không tìm thấy màu sắc.");
            }

            var colorName = dto.ColorName.Trim().ToLower();

            var exists = await _context.Colors.AnyAsync(x =>
                x.ColorName.ToLower() == colorName &&
                x.ColorId != colorId);

            if (exists)
            {
                throw new BadRequestException("Tên màu đã tồn tại.");
            }

            color.ColorName = dto.ColorName.Trim();
            color.ColorCode = NormalizeNullableText(dto.ColorCode);

            await _context.SaveChangesAsync();

            return MapToDto(color);
        }

        public async Task DeleteAsync(int colorId)
        {
            var color = await _context.Colors
                .FirstOrDefaultAsync(x => x.ColorId == colorId);

            if (color is null)
            {
                throw new NotFoundException("Không tìm thấy màu sắc.");
            }

            _context.Colors.Remove(color);
            await _context.SaveChangesAsync();
        }

        private static string? NormalizeNullableText(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static ColorDto MapToDto(Color color)
        {
            return new ColorDto
            {
                ColorId = color.ColorId,
                ColorName = color.ColorName,
                ColorCode = color.ColorCode
            };
        }
    }
}
