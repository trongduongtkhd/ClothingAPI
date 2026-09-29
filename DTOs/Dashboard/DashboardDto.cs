namespace ClothingAPI.DTOs.Dashboard;

public class DashboardDto
{
    public decimal TotalRevenue { get; set; }

    // Giá vốn hàng bán của các đơn đã hoàn thành.
    public decimal TotalCostOfGoodsSold { get; set; }

    public decimal TotalProfit { get; set; }

    // Tỷ suất lợi nhuận trên doanh thu (%).
    public decimal ProfitMarginPercent { get; set; }

    public int TotalOrders { get; set; }

    public int TotalCustomers { get; set; }

    public int PendingOrders { get; set; }

    public int LowStockProducts { get; set; }

    public List<RecentOrderDto> RecentOrders { get; set; } = [];

    public List<BestSellingProductDto> BestSellingProducts { get; set; } = [];

    public List<LowStockVariantDto> LowStockVariants { get; set; } = [];
}