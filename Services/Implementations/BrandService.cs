using ClothingAPI.Data;
using ClothingAPI.DTOs.Brands;
using ClothingAPI.Exceptions;
using ClothingAPI.Models;
using ClothingAPI.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
namespace ClothingAPI.Services.Implementations
{
    public class BrandService : IBrandService
    {
        private readonly AppDbContext _context;

        public BrandService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<BrandDto>> GetAllAsync()
        {
        
            var brands = await _context.Brands
                 .AsNoTracking()
                .OrderBy(x => x.BrandName)
                .ToListAsync();
            return brands.Select(MapToDto).ToList();

        }

        public async Task<BrandDto> GetByIdAsync(int brandId)
        {
            var brand = await _context.Brands
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.BrandId == brandId);

            if (brand is null)
            {
                throw new NotFoundException("Không tìm thấy thương hiệu.");
            }

            return MapToDto(brand);
        }

        public async Task<BrandDto> CreateAsync(UpsertBrandDto dto)
        {
            var brandName = dto.BrandName.Trim().ToLower();

            var exists = await _context.Brands.AnyAsync(x => x.BrandName.ToLower() == brandName);

            if (exists)
            {
                throw new BadRequestException("Tên thương hiệu đã tồn tại.");
            }

            var brand = new Brand
            {
                BrandName = dto.BrandName.Trim(),
                Description = NormalizeNullableText(dto.Description),
                LogoUrl = NormalizeNullableText(dto.LogoUrl),
                IsActive = dto.IsActive,
                CreatedAt = DateTime.UtcNow
            };

            _context.Brands.Add(brand);
            await _context.SaveChangesAsync();

            return MapToDto(brand);
        }

        public async Task<BrandDto> UpdateAsync(int brandId, UpsertBrandDto dto)
        {
            var brand = await _context.Brands
                .FirstOrDefaultAsync(x => x.BrandId == brandId);

            if (brand is null)
            {
                throw new NotFoundException("Không tìm thấy thương hiệu.");
            }

            var brandName = dto.BrandName.Trim().ToLower();

            var exists = await _context.Brands.AnyAsync(x =>
                x.BrandName.ToLower() == brandName &&
                x.BrandId != brandId);

            if (exists)
            {
                throw new BadRequestException("Tên thương hiệu đã tồn tại.");
            }

            brand.BrandName = dto.BrandName.Trim();
            brand.Description = NormalizeNullableText(dto.Description);
            brand.LogoUrl = NormalizeNullableText(dto.LogoUrl);
            brand.IsActive = dto.IsActive;

            await _context.SaveChangesAsync();

            return MapToDto(brand);
        }

        public async Task DeleteAsync(int brandId)
        {
            var brand = await _context.Brands
                .FirstOrDefaultAsync(x => x.BrandId == brandId);

            if (brand is null)
            {
                throw new NotFoundException("Không tìm thấy thương hiệu.");
            }

            // Xóa mềm để không ảnh hưởng Product trong tương lai.
            brand.IsActive = false;

            await _context.SaveChangesAsync();
        }

        private static string? NormalizeNullableText(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static BrandDto MapToDto(Brand brand)
        {
            return new BrandDto
            {
                BrandId = brand.BrandId,
                BrandName = brand.BrandName,
                Description = brand.Description,
                LogoUrl = brand.LogoUrl,
                IsActive = brand.IsActive,
                CreatedAt = brand.CreatedAt
            };
        }
    }
}
