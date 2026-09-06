using ClothingAPI.Data;
using ClothingAPI.DTOs.Categories;
using ClothingAPI.Exceptions;
using ClothingAPI.Helpers;
using ClothingAPI.Models;
using ClothingAPI.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
namespace ClothingAPI.Services.Implementations
{
    public class CategoryService : ICategoryService
    {
        private readonly AppDbContext _context;

        public CategoryService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<CategoryDto>> GetAllAsync()
        {

            var categories = await _context.Categories
              .AsNoTracking()
              .Include(x => x.ParentCategory)
              .OrderBy(x => x.ParentCategoryId)
               .ThenBy(x => x.CategoryName)
              .ToListAsync();

            return categories.Select(MapToDto).ToList();
        }

        public async Task<CategoryDto> GetByIdAsync(int categoryId)
        {
            var category = await _context.Categories
                .AsNoTracking()
                .Include(x => x.ParentCategory)
                .FirstOrDefaultAsync(x => x.CategoryId == categoryId);

            if (category is null)
            {
                throw new NotFoundException("Không tìm thấy danh mục.");
            }

            return MapToDto(category);
        }

        public async Task<CategoryDto> CreateAsync(UpsertCategoryDto dto)
        {
            await ValidateParentCategoryAsync(dto.ParentCategoryId);

            var slug = await GenerateUniqueSlugAsync(dto.CategoryName);

            var category = new Category
            {
                ParentCategoryId = dto.ParentCategoryId,
                CategoryName = dto.CategoryName.Trim(),
                Slug = slug,
                Description = NormalizeNullableText(dto.Description),
                ImageUrl = NormalizeNullableText(dto.ImageUrl),
                IsActive = dto.IsActive,
                CreatedAt = DateTime.UtcNow
            };

            _context.Categories.Add(category);
            await _context.SaveChangesAsync();

            return await GetByIdAsync(category.CategoryId);
        }

        public async Task<CategoryDto> UpdateAsync(int categoryId, UpsertCategoryDto dto)
        {
            var category = await _context.Categories
                .FirstOrDefaultAsync(x => x.CategoryId == categoryId);

            if (category is null)
            {
                throw new NotFoundException("Không tìm thấy danh mục.");
            }

            if (dto.ParentCategoryId == categoryId)
            {
                throw new BadRequestException("Danh mục không thể là danh mục cha của chính nó.");
            }

            await ValidateParentCategoryAsync(dto.ParentCategoryId);

            if (dto.ParentCategoryId.HasValue && await IsDescendantAsync(dto.ParentCategoryId.Value, categoryId))
            {
                throw new BadRequestException(
                    "Không thể chọn danh mục con làm danh mục cha."
                );
            }

            category.ParentCategoryId = dto.ParentCategoryId;
            category.CategoryName = dto.CategoryName.Trim();
            category.Slug = await GenerateUniqueSlugAsync(dto.CategoryName, categoryId
            );
            category.Description = NormalizeNullableText(dto.Description);
            category.ImageUrl = NormalizeNullableText(dto.ImageUrl);
            category.IsActive = dto.IsActive;

            await _context.SaveChangesAsync();

            return await GetByIdAsync(categoryId);
        }

        public async Task DeleteAsync(int categoryId)
        {
            var category = await _context.Categories
                .Include(x => x.ChildCategories)
                .FirstOrDefaultAsync(x => x.CategoryId == categoryId);

            if (category is null)
            {
                throw new NotFoundException("Không tìm thấy danh mục.");
            }

            if (category.ChildCategories.Any())
            {
                throw new BadRequestException(
                    "Không thể xóa danh mục đang có danh mục con."
                );
            }

            // Xóa mềm: danh mục sẽ không xuất hiện ở website khách hàng.
            category.IsActive = false;

            await _context.SaveChangesAsync();
        }

        private async Task ValidateParentCategoryAsync(int? parentCategoryId)
        {
            if (!parentCategoryId.HasValue)
            {
                return;
            }

            var parentExists = await _context.Categories.AnyAsync(x => x.CategoryId == parentCategoryId.Value);

            if (!parentExists)
            {
                throw new BadRequestException("Danh mục cha không tồn tại.");
            }
        }

        private async Task<bool> IsDescendantAsync(int possibleParentId, int categoryId)
        {
            var currentCategoryId = possibleParentId;

            while (true)
            {
                var current = await _context.Categories.AsNoTracking()
                    .Select(x => new
                    {
                        x.CategoryId,
                        x.ParentCategoryId
                    })
                    .FirstOrDefaultAsync(x => x.CategoryId == currentCategoryId);

                if (current is null || !current.ParentCategoryId.HasValue)
                {
                    return false;
                }

                if (current.ParentCategoryId.Value == categoryId)
                {
                    return true;
                }

                currentCategoryId = current.ParentCategoryId.Value;
            }
        }

        private async Task<string> GenerateUniqueSlugAsync(string categoryName, int? excludedCategoryId = null)
        {
            var baseSlug = SlugHelper.Generate(categoryName);

            if (string.IsNullOrWhiteSpace(baseSlug))
            {
                throw new BadRequestException("Không thể tạo slug từ tên danh mục.");
            }

            var slug = baseSlug;
            var number = 2;

            while (await _context.Categories.AnyAsync(x =>
                x.Slug == slug &&
                (!excludedCategoryId.HasValue ||
                 x.CategoryId != excludedCategoryId.Value)))
            {
                slug = $"{baseSlug}-{number}";
                number++;
            }

            return slug;
        }

        private static string? NormalizeNullableText(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static CategoryDto MapToDto(Category category)
        {
            return new CategoryDto
            {
                CategoryId = category.CategoryId,
                ParentCategoryId = category.ParentCategoryId,
                ParentCategoryName = category.ParentCategory?.CategoryName,
                CategoryName = category.CategoryName,
                Slug = category.Slug,
                Description = category.Description,
                ImageUrl = category.ImageUrl,
                IsActive = category.IsActive,
                CreatedAt = category.CreatedAt
            };
        }
    }
}
