using System.ComponentModel.DataAnnotations;

namespace ClothingAPI.DTOs.PurchaseOrders
{
    public class CancelPurchaseOrderDto
    {
        [Required(ErrorMessage = "Vui lòng nhập lý do hủy phiếu.")]
        [StringLength(500)]
        public string Reason { get; set; } = string.Empty;
    }
}
