using ClothingAPI.Data;
using ClothingAPI.DTOs.Wishlist;
using ClothingAPI.Exceptions;
using ClothingAPI.Models;
using ClothingAPI.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClothingAPI.Services.Implementations
{
    public class WishlistService : IWishlistService
    {
        private readonly AppDbContext _context;

        public WishlistService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<WishlistItemDto>> GetMyWishlistAsync(int userId)
        {
            var items = await _context.WishlistItems
                .AsNoTracking()
                .Include(x => x.Product)
                    .ThenInclude(x => x.Category)
                .Include(x => x.Product)
                    .ThenInclude(x => x.Brand)
                .Include(x => x.Product)
                    .ThenInclude(x => x.ProductImages)
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();

            return items.Select(MapDto).ToList();
        }

        public async Task<WishlistItemDto> AddItemAsync(int userId, int productId)
        {
            var product = await _context.Products
                .Include(x => x.Category)
                .Include(x => x.Brand)
                .Include(x => x.ProductImages)
                .FirstOrDefaultAsync(x => x.ProductId == productId);

            if (product is null || !product.IsActive)
            {
                throw new NotFoundException("Không tìm thấy sản phẩm.");
            }

            var existing = await _context.WishlistItems
                .FirstOrDefaultAsync(x => x.UserId == userId && x.ProductId == productId);

            if (existing is not null)
            {
                throw new BadRequestException("Sản phẩm đã có trong danh sách yêu thích.");
            }

            var wishlistItem = new WishlistItem
            {
                UserId = userId,
                ProductId = productId,
                CreatedAt = DateTime.UtcNow
            };

            _context.WishlistItems.Add(wishlistItem);
            await _context.SaveChangesAsync();

            wishlistItem.Product = product;

            return MapDto(wishlistItem);
        }

        public async Task RemoveItemAsync(int userId, int productId)
        {
            var wishlistItem = await _context.WishlistItems
                .FirstOrDefaultAsync(x => x.UserId == userId && x.ProductId == productId);

            if (wishlistItem is null)
            {
                throw new NotFoundException("Không tìm thấy sản phẩm trong danh sách yêu thích.");
            }

            _context.WishlistItems.Remove(wishlistItem);
            await _context.SaveChangesAsync();
        }

        private static WishlistItemDto MapDto(WishlistItem wishlistItem)
        {
            var product = wishlistItem.Product;

            var thumbnailUrl = product.ProductImages
                .Where(x => x.IsThumbnail)
                .OrderBy(x => x.DisplayOrder)
                .Select(x => x.ImageUrl)
                .FirstOrDefault()
                ?? product.ProductImages
                    .OrderBy(x => x.DisplayOrder)
                    .Select(x => x.ImageUrl)
                    .FirstOrDefault();

            return new WishlistItemDto
            {
                ProductId = product.ProductId,
                ProductName = product.ProductName,
                Slug = product.Slug,
                CategoryName = product.Category.CategoryName,
                BrandName = product.Brand?.BrandName,
                BasePrice = product.BasePrice,
                SalePrice = product.SalePrice,
                ThumbnailUrl = thumbnailUrl,
                IsFeatured = product.IsFeatured,
                CreatedAt = wishlistItem.CreatedAt
            };
        }
    }
}
