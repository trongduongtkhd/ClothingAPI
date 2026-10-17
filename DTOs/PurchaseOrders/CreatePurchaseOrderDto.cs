using System.ComponentModel.DataAnnotations;

namespace ClothingAPI.DTOs.PurchaseOrders
{
    public class CreatePurchaseOrderDto
    {
        [Required(ErrorMessage = "Vui lòng chọn nhà cung ứng.")]
        public int SupplierId { get; set; }

        [StringLength(500)]
        public string? Note { get; set; }

        [Required(ErrorMessage = "Phiếu nhập phải có ít nhất 1 sản phẩm.")]
        [MinLength(1, ErrorMessage = "Phiếu nhập phải có ít nhất 1 sản phẩm.")]
        public List<CreatePurchaseOrderItemDto> Items { get; set; } = [];
    }

    public class CreatePurchaseOrderItemDto
    {
        [Required(ErrorMessage = "Vui lòng chọn biến thể sản phẩm.")]
        public int VariantId { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Số lượng phải lớn hơn 0.")]
        public int Quantity { get; set; }

        [Range(typeof(decimal), "0.01", "9999999999999999", ErrorMessage = "Đơn giá nhập phải lớn hơn 0.")]
        public decimal UnitCost { get; set; }
    }
}
