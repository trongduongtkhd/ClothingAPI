using ClothingAPI.DTOs.PurchaseOrders;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ClothingAPI.Controllers
{
    [Route("api/admin/purchase-orders")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AdminPurchaseOrdersController : ControllerBase
    {
        private readonly IPurchaseOrderService _purchaseOrderService;

        public AdminPurchaseOrdersController(IPurchaseOrderService purchaseOrderService)
        {
            _purchaseOrderService = purchaseOrderService;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<PagedResult<PurchaseOrderListDto>>>> GetAll(
            [FromQuery] int? supplierId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var result = await _purchaseOrderService.GetAllAsync(supplierId, page, pageSize);

            return Ok(new ApiResponse<PagedResult<PurchaseOrderListDto>>(
                true,
                "Lấy danh sách phiếu nhập kho thành công.",
                result
            ));
        }

        [HttpGet("{purchaseOrderId:int}")]
        public async Task<ActionResult<ApiResponse<PurchaseOrderDetailDto>>> GetById(int purchaseOrderId)
        {
            var result = await _purchaseOrderService.GetByIdAsync(purchaseOrderId);

            return Ok(new ApiResponse<PurchaseOrderDetailDto>(
                true,
                "Lấy chi tiết phiếu nhập kho thành công.",
                result
            ));
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<PurchaseOrderDetailDto>>> Create(
            [FromBody] CreatePurchaseOrderDto dto)
        {
            var userId = User.GetUserId();

            var result = await _purchaseOrderService.CreateAsync(userId, dto);

            return StatusCode(StatusCodes.Status201Created,
                new ApiResponse<PurchaseOrderDetailDto>(
                    true,
                    "Tạo phiếu nhập kho thành công.",
                    result
                ));
        }

        [HttpPut("{purchaseOrderId:int}")]
        public async Task<ActionResult<ApiResponse<PurchaseOrderDetailDto>>> Update(
            int purchaseOrderId,
            [FromBody] CreatePurchaseOrderDto dto)
        {
            var result = await _purchaseOrderService.UpdateAsync(purchaseOrderId, dto);

            return Ok(new ApiResponse<PurchaseOrderDetailDto>(
                true,
                "Cập nhật phiếu nhập kho thành công.",
                result
            ));
        }

        [HttpPost("{purchaseOrderId:int}/cancel")]
        public async Task<ActionResult<ApiResponse<PurchaseOrderDetailDto>>> Cancel(
            int purchaseOrderId,
            [FromBody] CancelPurchaseOrderDto dto)
        {
            var result = await _purchaseOrderService.CancelAsync(purchaseOrderId, dto);

            return Ok(new ApiResponse<PurchaseOrderDetailDto>(
                true,
                "Hủy phiếu nhập kho thành công.",
                result
            ));
        }
    }
}
