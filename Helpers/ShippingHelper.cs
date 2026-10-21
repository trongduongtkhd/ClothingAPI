namespace ClothingAPI.Helpers
{
    // Quy tắc phí vận chuyển dùng chung cho giỏ hàng (hiển thị trước) và đơn hàng (tính thật).
    // Phí tính trên tạm tính trước giảm giá.
    public static class ShippingHelper
    {
        public const decimal StandardShippingFee = 30000m;
        public const decimal FreeShippingThreshold = 500000m;

        public static decimal CalculateShippingFee(decimal subtotal)
        {
            if (subtotal <= 0m)
            {
                return 0m;
            }

            return subtotal >= FreeShippingThreshold ? 0m : StandardShippingFee;
        }
    }
}
