using ClothingAPI.DTOs.Addresses;

namespace ClothingAPI.Services.Interfaces
{
    public interface IAddressService
    {
        Task<List<AddressDto>> GetMyAddressesAsync(int userId);
        Task<AddressDto> CreateAsync(int userId, UpsertAddressDto dto);
        Task<AddressDto> UpdateAsync(int userId, int addressId, UpsertAddressDto dto);
        Task SetDefaultAsync(int userId, int addressId);
        Task DeleteAsync(int userId, int addressId);
    }
}
