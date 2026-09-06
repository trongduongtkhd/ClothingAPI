using ClothingAPI.DTOs.Addresses;
using ClothingAPI.Helpers;
using ClothingAPI.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClothingAPI.Controllers;

[ApiController]
[Route("api/addresses")]
[Authorize]
public class AddressesController : ControllerBase
{
    private readonly IAddressService _addressService;

    public AddressesController(IAddressService addressService)
    {
        _addressService = addressService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<AddressDto>>>> GetMyAddresses()
    {
        var userId = User.GetUserId();

        var result = await _addressService.GetMyAddressesAsync(userId);

        return Ok(new ApiResponse<List<AddressDto>>(
            true,
            "Lấy danh sách địa chỉ thành công.",
            result
        ));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<AddressDto>>> Create(
        [FromBody] UpsertAddressDto dto)
    {
        var userId = User.GetUserId();

        var result = await _addressService.CreateAsync(userId, dto);

        return StatusCode(StatusCodes.Status201Created,
            new ApiResponse<AddressDto>(
                true,
                "Thêm địa chỉ thành công.",
                result
            ));
    }

    [HttpPut("{addressId:int}")]
    public async Task<ActionResult<ApiResponse<AddressDto>>> Update(
        int addressId,
        [FromBody] UpsertAddressDto dto)
    {
        var userId = User.GetUserId();

        var result = await _addressService.UpdateAsync(userId, addressId, dto);

        return Ok(new ApiResponse<AddressDto>(
            true,
            "Cập nhật địa chỉ thành công.",
            result
        ));
    }

    [HttpPut("{addressId:int}/set-default")]
    public async Task<ActionResult<ApiResponse<object>>> SetDefault(int addressId)
    {
        var userId = User.GetUserId();

        await _addressService.SetDefaultAsync(userId, addressId);

        return Ok(new ApiResponse<object>(
            true,
            "Đặt địa chỉ mặc định thành công."
        ));
    }

    [HttpDelete("{addressId:int}")]
    public async Task<ActionResult<ApiResponse<object>>> Delete(int addressId)
    {
        var userId = User.GetUserId();

        await _addressService.DeleteAsync(userId, addressId);

        return Ok(new ApiResponse<object>(
            true,
            "Xóa địa chỉ thành công."
        ));
    }
}