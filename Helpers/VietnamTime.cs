namespace ClothingAPI.Helpers
{
    // Giờ Việt Nam (UTC+7, không có giờ mùa hè). Dữ liệu vẫn lưu UTC; helper này dùng cho
    // những chỗ cần "ngày/giờ theo VN": mã đơn hàng, lọc theo ngày lịch.
    public static class VietnamTime
    {
        public static readonly TimeSpan Offset = TimeSpan.FromHours(7);

        public static DateTime Now => DateTime.SpecifyKind(DateTime.UtcNow + Offset, DateTimeKind.Unspecified);

        // 00:00 của ngày lịch VN, quy ra UTC.
        public static DateTime StartOfDayUtc(DateTime vietnamDate)
        {
            return DateTime.SpecifyKind(vietnamDate.Date - Offset, DateTimeKind.Utc);
        }
    }
}
