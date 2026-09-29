using ClothingAPI.Data;
using ClothingAPI.DTOs.Suppliers;
using ClothingAPI.Exceptions;
using ClothingAPI.Models;
using ClothingAPI.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClothingAPI.Services.Implementations
{
    public class SupplierService : ISupplierService
    {
        private readonly AppDbContext _context;

        public SupplierService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<SupplierDto>> GetAllAsync()
        {
            var suppliers = await _context.Suppliers
                .AsNoTracking()
                .OrderBy(x => x.SupplierName)
                .ToListAsync();

            return suppliers.Select(MapToDto).ToList();
        }

        public async Task<SupplierDto> GetByIdAsync(int supplierId)
        {
            var supplier = await _context.Suppliers
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.SupplierId == supplierId);

            if (supplier is null)
            {
                throw new NotFoundException("Không tìm thấy nhà cung ứng.");
            }

            return MapToDto(supplier);
        }

        public async Task<SupplierDto> CreateAsync(UpsertSupplierDto dto)
        {
            var supplierName = dto.SupplierName.Trim().ToLower();

            var exists = await _context.Suppliers.AnyAsync(x => x.SupplierName.ToLower() == supplierName);

            if (exists)
            {
                throw new BadRequestException("Tên nhà cung ứng đã tồn tại.");
            }

            var supplier = new Supplier
            {
                SupplierName = dto.SupplierName.Trim(),
                ContactName = NormalizeNullableText(dto.ContactName),
                Phone = NormalizeNullableText(dto.Phone),
                Email = NormalizeNullableText(dto.Email),
                Address = NormalizeNullableText(dto.Address),
                TaxCode = NormalizeNullableText(dto.TaxCode),
                Description = NormalizeNullableText(dto.Description),
                IsActive = dto.IsActive,
                CreatedAt = DateTime.UtcNow
            };

            _context.Suppliers.Add(supplier);
            await _context.SaveChangesAsync();

            return MapToDto(supplier);
        }

        public async Task<SupplierDto> UpdateAsync(int supplierId, UpsertSupplierDto dto)
        {
            var supplier = await _context.Suppliers
                .FirstOrDefaultAsync(x => x.SupplierId == supplierId);

            if (supplier is null)
            {
                throw new NotFoundException("Không tìm thấy nhà cung ứng.");
            }

            var supplierName = dto.SupplierName.Trim().ToLower();

            var exists = await _context.Suppliers.AnyAsync(x =>
                x.SupplierName.ToLower() == supplierName &&
                x.SupplierId != supplierId);

            if (exists)
            {
                throw new BadRequestException("Tên nhà cung ứng đã tồn tại.");
            }

            supplier.SupplierName = dto.SupplierName.Trim();
            supplier.ContactName = NormalizeNullableText(dto.ContactName);
            supplier.Phone = NormalizeNullableText(dto.Phone);
            supplier.Email = NormalizeNullableText(dto.Email);
            supplier.Address = NormalizeNullableText(dto.Address);
            supplier.TaxCode = NormalizeNullableText(dto.TaxCode);
            supplier.Description = NormalizeNullableText(dto.Description);
            supplier.IsActive = dto.IsActive;

            await _context.SaveChangesAsync();

            return MapToDto(supplier);
        }

        public async Task DeleteAsync(int supplierId)
        {
            var supplier = await _context.Suppliers
                .FirstOrDefaultAsync(x => x.SupplierId == supplierId);

            if (supplier is null)
            {
                throw new NotFoundException("Không tìm thấy nhà cung ứng.");
            }

            // Xóa mềm để không ảnh hưởng lịch sử phiếu nhập kho đã có.
            supplier.IsActive = false;

            await _context.SaveChangesAsync();
        }

        private static string? NormalizeNullableText(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static SupplierDto MapToDto(Supplier supplier)
        {
            return new SupplierDto
            {
                SupplierId = supplier.SupplierId,
                SupplierName = supplier.SupplierName,
                ContactName = supplier.ContactName,
                Phone = supplier.Phone,
                Email = supplier.Email,
                Address = supplier.Address,
                TaxCode = supplier.TaxCode,
                Description = supplier.Description,
                IsActive = supplier.IsActive,
                CreatedAt = supplier.CreatedAt
            };
        }
    }
}
