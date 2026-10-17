using ClothingAPI.Models;

namespace ClothingAPI.Helpers
{
    // Biến thể để trống Price nghĩa là bán theo giá của sản phẩm (cả giá gốc lẫn giá giảm).
    // Khi biến thể có Price riêng thì dùng cặp Price/SalePrice của chính biến thể.
    // Các overload không nhận product yêu cầu variant.Product đã được nạp.
    public static class PriceHelper
    {
        public static decimal GetEffectivePrice(ProductVariant variant, Product product)
        {
            return variant.Price ?? product.BasePrice;
        }

        public static decimal? GetEffectiveSalePrice(ProductVariant variant, Product product)
        {
            return variant.Price.HasValue ? variant.SalePrice : product.SalePrice;
        }

        // Đơn giá khách thực trả.
        public static decimal GetUnitPrice(ProductVariant variant)
        {
            return GetEffectiveSalePrice(variant, variant.Product) ?? GetEffectivePrice(variant, variant.Product);
        }
    }
}
