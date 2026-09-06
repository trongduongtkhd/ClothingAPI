using ClothingAPI.Data;
using ClothingAPI.DTOs.Cart;
using ClothingAPI.Exceptions;
using ClothingAPI.Models;
using ClothingAPI.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClothingAPI.Services.Implementations;

public class CartService : ICartService
{
    private readonly AppDbContext _context;

    public CartService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<CartDto> GetMyCartAsync(int userId)
    {
        return await BuildCartAsync(userId);
    }

    public async Task<CartDto> AddItemAsync(int userId, AddCartItemDto dto)
    {
        var variant = await _context.ProductVariants
            .Include(x => x.Product)
            .FirstOrDefaultAsync(x => x.VariantId == dto.VariantId);

        if (variant is null)
        {
            throw new NotFoundException("Không tìm thấy biến thể sản phẩm.");
        }

        if (!variant.IsActive || !variant.Product.IsActive)
        {
            throw new BadRequestException(
                "Biến thể sản phẩm hiện không còn được bán."
            );
        }

        if (variant.StockQuantity <= 0)
        {
            throw new BadRequestException("Sản phẩm đã hết hàng.");
        }

        var cartItem = await _context.CartItems
            .FirstOrDefaultAsync(x => x.UserId == userId && x.VariantId == dto.VariantId);

        var finalQuantity = dto.Quantity;

        if (cartItem is not null)
        {
            finalQuantity += cartItem.Quantity;
        }

        if (finalQuantity > variant.StockQuantity)
        {
            throw new BadRequestException(
                $"Số lượng trong giỏ không được vượt quá tồn kho ({variant.StockQuantity})."
            );
        }

        if (cartItem is null)
        {
            cartItem = new CartItem
            {
                UserId = userId,
                VariantId = dto.VariantId,
                Quantity = dto.Quantity,
                CreatedAt = DateTime.UtcNow
            };

            _context.CartItems.Add(cartItem);
        }
        else
        {
            cartItem.Quantity = finalQuantity;
            cartItem.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        return await BuildCartAsync(userId);
    }

    public async Task<CartDto> UpdateItemAsync(int userId, int cartItemId, UpdateCartItemDto dto)
    {
        var cartItem = await _context.CartItems
            .Include(x => x.Variant)
                .ThenInclude(x => x.Product)
            .FirstOrDefaultAsync(x => x.CartItemId == cartItemId && x.UserId == userId);

        if (cartItem is null)
        {
            throw new NotFoundException("Không tìm thấy sản phẩm trong giỏ hàng.");
        }

        if (!cartItem.Variant.IsActive || !cartItem.Variant.Product.IsActive)
        {
            throw new BadRequestException(
                "Biến thể sản phẩm hiện không còn được bán."
            );
        }

        if (dto.Quantity > cartItem.Variant.StockQuantity)
        {
            throw new BadRequestException(
                $"Số lượng trong giỏ không được vượt quá tồn kho ({cartItem.Variant.StockQuantity})."
            );
        }

        cartItem.Quantity = dto.Quantity;
        cartItem.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return await BuildCartAsync(userId);
    }

    public async Task DeleteItemAsync(int userId, int cartItemId)
    {
        var cartItem = await _context.CartItems
            .FirstOrDefaultAsync(x =>
                x.CartItemId == cartItemId &&
                x.UserId == userId);

        if (cartItem is null)
        {
            throw new NotFoundException("Không tìm thấy sản phẩm trong giỏ hàng.");
        }

        _context.CartItems.Remove(cartItem);
        await _context.SaveChangesAsync();
    }

    public async Task ClearCartAsync(int userId)
    {
        var cartItems = await _context.CartItems
            .Where(x => x.UserId == userId)
            .ToListAsync();

        if (cartItems.Count == 0)
        {
            return;
        }

        _context.CartItems.RemoveRange(cartItems);
        await _context.SaveChangesAsync();
    }

    private async Task<CartDto> BuildCartAsync(int userId)
    {
        var cartItems = await _context.CartItems
            .AsNoTracking()
            .Include(x => x.Variant)
                .ThenInclude(x => x.Product)
                    .ThenInclude(x => x.ProductImages)
            .Include(x => x.Variant)
                .ThenInclude(x => x.Color)
            .Include(x => x.Variant)
                .ThenInclude(x => x.Size)
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        var itemDtos = cartItems.Select(MapCartItemDto).ToList();

        return new CartDto
        {
            Items = itemDtos,
            Subtotal = itemDtos.Sum(x => x.TotalPrice),
            TotalQuantity = itemDtos.Sum(x => x.Quantity)
        };
    }

    private static CartItemDto MapCartItemDto(CartItem cartItem)
    {
        var variant = cartItem.Variant;
        var product = variant.Product;

        var thumbnailUrl = variant.ImageUrl
            ?? product.ProductImages
                .Where(x => x.IsThumbnail)
                .OrderBy(x => x.DisplayOrder)
                .Select(x => x.ImageUrl)
                .FirstOrDefault()
            ?? product.ProductImages
                .OrderBy(x => x.DisplayOrder)
                .Select(x => x.ImageUrl)
                .FirstOrDefault();

        var unitPrice = variant.SalePrice ?? variant.Price;

        return new CartItemDto
        {
            CartItemId = cartItem.CartItemId,
            VariantId = variant.VariantId,
            ProductId = product.ProductId,
            ProductName = product.ProductName,
            ThumbnailUrl = thumbnailUrl,
            ColorName = variant.Color.ColorName,
            SizeName = variant.Size.SizeName,
            UnitPrice = unitPrice,
            StockQuantity = variant.StockQuantity,
            Quantity = cartItem.Quantity,
            TotalPrice = unitPrice * cartItem.Quantity,
            IsAvailable = variant.IsActive && product.IsActive && variant.StockQuantity >= cartItem.Quantity
        };
    }
}