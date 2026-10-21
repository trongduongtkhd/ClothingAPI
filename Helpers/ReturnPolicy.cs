using ClothingAPI.DTOs.Orders;
using ClothingAPI.Enums;
using ClothingAPI.Models;

namespace ClothingAPI.Helpers
{
    // Chính sách trả hàng: đơn đã hoàn thành + đã thanh toán, trong WindowDays ngày kể từ lúc hoàn thành,
    // mỗi đơn tối đa 1 yêu cầu. Cần nạp sẵn OrderStatusHistories và ReturnRequest của order.
    public static class ReturnPolicy
    {
        public const int WindowDays = 7;

        public static DateTime? GetCompletedAt(Order order)
        {
            if (order.OrderStatus != OrderStatus.Completed)
            {
                return null;
            }

            var completedHistory = order.OrderStatusHistories
                .Where(x => x.Status == OrderStatus.Completed)
                .OrderBy(x => x.CreatedAt)
                .FirstOrDefault();

            return completedHistory?.CreatedAt ?? order.UpdatedAt ?? order.CreatedAt;
        }

        public static DateTime? GetDeadline(Order order)
        {
            return GetCompletedAt(order)?.AddDays(WindowDays);
        }

        public static bool CanRequest(Order order, DateTime nowUtc)
        {
            var deadline = GetDeadline(order);

            return deadline.HasValue &&
                   nowUtc <= deadline.Value &&
                   order.PaymentStatus == PaymentStatus.Paid &&
                   order.ReturnRequest is null;
        }

        public static ReturnRequestDto? ToDto(ReturnRequest? request)
        {
            if (request is null)
            {
                return null;
            }

            return new ReturnRequestDto
            {
                ReturnRequestId = request.ReturnRequestId,
                Reason = request.Reason,
                Status = request.Status,
                AdminNote = request.AdminNote,
                CreatedAt = request.CreatedAt,
                ProcessedAt = request.ProcessedAt
            };
        }

        // Ghi chú lịch sử trạng thái tối đa 500 ký tự.
        public static string TrimNote(string note)
        {
            return note.Length <= 500 ? note : note[..500];
        }
    }
}
