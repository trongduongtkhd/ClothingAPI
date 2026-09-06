namespace ClothingAPI.DTOs.Orders
{
    public class MockQrDto
    {
        public string OrderCode { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        public string BankName { get; set; } = "DEMO BANK";

        public string AccountNumber { get; set; } = "123456789";

        public string TransferContent { get; set; } = string.Empty;

        public string Message { get; set; } = "Đây là thanh toán QR mô phỏng phục vụ đồ án.";

    }
}
