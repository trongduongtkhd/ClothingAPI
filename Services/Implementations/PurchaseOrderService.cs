using ClothingAPI.Data;
using ClothingAPI.DTOs.PurchaseOrders;
using ClothingAPI.Exceptions;
using ClothingAPI.Helpers;
using ClothingAPI.Models;
using ClothingAPI.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ClothingAPI.Services.Implementations
{
    public class PurchaseOrderService : IPurchaseOrderService
    {
        private readonly AppDbContext _context;

        public PurchaseOrderService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<PagedResult<PurchaseOrderListDto>> GetAllAsync(int? supplierId, int page, int pageSize)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 or > 100 ? 20 : pageSize;

            var query = _context.PurchaseOrders
                .AsNoTracking()
                .Include(x => x.Supplier)
                .Include(x => x.CreatedByUser)
                .Include(x => x.Items)
                .AsQueryable();

            if (supplierId.HasValue)
            {
                query = query.Where(x => x.SupplierId == supplierId.Value);
            }

            var totalItems = await query.CountAsync();

            var purchaseOrders = await query
                .OrderByDescending(x => x.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var items = purchaseOrders.Select(x => new PurchaseOrderListDto
            {
                PurchaseOrderId = x.PurchaseOrderId,
                PurchaseOrderCode = x.PurchaseOrderCode,
                SupplierId = x.SupplierId,
                SupplierName = x.Supplier.SupplierName,
                CreatedByUserName = x.CreatedByUser.FullName,
                TotalItemCount = x.Items.Count,
                TotalQuantity = x.Items.Sum(i => i.Quantity),
                TotalAmount = x.TotalAmount,
                Status = x.Status,
                CreatedAt = x.CreatedAt
            }).ToList();

            return new PagedResult<PurchaseOrderListDto>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalItems = totalItems,
                TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize)
            };
        }

        public async Task<PurchaseOrderDetailDto> GetByIdAsync(int purchaseOrderId)
        {
            var purchaseOrder = await _context.PurchaseOrders
                .AsNoTracking()
                .Include(x => x.Supplier)
                .Include(x => x.CreatedByUser)
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.PurchaseOrderId == purchaseOrderId);

            if (purchaseOrder is null)
            {
                throw new NotFoundException("Không tìm thấy phiếu nhập kho.");
            }

            return MapToDetailDto(purchaseOrder);
        }

        public async Task<PurchaseOrderDetailDto> CreateAsync(int createdByUserId, CreatePurchaseOrderDto dto)
        {
            ValidateItems(dto);
            await using var transaction = await _context.Database.BeginTransactionAsync();

            var supplier = await _context.Suppliers
                .FirstOrDefaultAsync(x => x.SupplierId == dto.SupplierId);

            if (supplier is null)
            {
                throw new NotFoundException("Không tìm thấy nhà cung ứng.");
            }

            var variants = await LoadVariantsAsync(dto.Items.Select(x => x.VariantId));

            var purchaseOrder = new PurchaseOrder
            {
                PurchaseOrderCode = await GeneratePurchaseOrderCodeAsync(),
                SupplierId = dto.SupplierId,
                CreatedByUserId = createdByUserId,
                Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim(),
                Status = PurchaseOrderStatuses.Completed,
                CreatedAt = DateTime.UtcNow
            };

            AddItemsAndStockIn(purchaseOrder, dto.Items, variants);

            _context.PurchaseOrders.Add(purchaseOrder);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return await GetByIdAsync(purchaseOrder.PurchaseOrderId);
        }

        public async Task<PurchaseOrderDetailDto> UpdateAsync(int purchaseOrderId, CreatePurchaseOrderDto dto)
        {
            ValidateItems(dto);
            await using var transaction = await _context.Database.BeginTransactionAsync();
            await LockPurchaseOrderAsync(purchaseOrderId);

            var purchaseOrder = await _context.PurchaseOrders
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.PurchaseOrderId == purchaseOrderId);

            if (purchaseOrder is null)
            {
                throw new NotFoundException("Không tìm thấy phiếu nhập kho.");
            }

            EnsureNotCancelled(purchaseOrder);

            if (!await _context.Suppliers.AnyAsync(x => x.SupplierId == dto.SupplierId))
            {
                throw new NotFoundException("Không tìm thấy nhà cung ứng.");
            }

            var oldItems = purchaseOrder.Items.ToList();

            // Nạp cả biến thể của phiếu cũ (để hoàn tác) lẫn biến thể của phiếu mới (để nhập lại).
            var newVariantIds = dto.Items.Select(x => x.VariantId).Distinct().ToList();
            var variants = await LoadVariantsAsync(newVariantIds.Concat(oldItems.Select(x => x.VariantId)));

            // Tồn kho sau khi sửa = Tồn hiện tại − SL phiếu cũ + SL phiếu mới, không được âm
            // (trường hợp hàng của phiếu cũ đã bán bớt mà lại giảm số lượng nhập).
            foreach (var variant in variants)
            {
                var oldQuantity = oldItems.Where(x => x.VariantId == variant.VariantId).Sum(x => (long)x.Quantity);
                var newQuantity = dto.Items.Where(x => x.VariantId == variant.VariantId).Sum(x => (long)x.Quantity);
                var finalStock = variant.StockQuantity - oldQuantity + newQuantity;

                if (finalStock < 0)
                {
                    throw new BadRequestException(
                        $"Không thể sửa: biến thể {variant.SKU} hiện chỉ còn tồn {variant.StockQuantity}, " +
                        $"không đủ để giảm từ {oldQuantity} xuống {newQuantity} (hàng đã được bán bớt).");
                }
                if (finalStock > int.MaxValue)
                    throw new BadRequestException($"Không thể sửa: tồn kho của biến thể {variant.SKU} vượt giới hạn.");
            }

            // Hoàn tác phiếu cũ rồi nhập lại theo nội dung mới.
            foreach (var oldItem in oldItems)
            {
                var variant = variants.First(x => x.VariantId == oldItem.VariantId);
                ReverseStockIn(variant, oldItem.Quantity, oldItem.UnitCost);
            }

            _context.PurchaseOrderItems.RemoveRange(oldItems);
            purchaseOrder.Items.Clear();

            purchaseOrder.SupplierId = dto.SupplierId;
            purchaseOrder.Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
            purchaseOrder.UpdatedAt = DateTime.UtcNow;

            AddItemsAndStockIn(purchaseOrder, dto.Items, variants);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return await GetByIdAsync(purchaseOrder.PurchaseOrderId);
        }

        public async Task<PurchaseOrderDetailDto> CancelAsync(int purchaseOrderId, CancelPurchaseOrderDto dto)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            await LockPurchaseOrderAsync(purchaseOrderId);

            var purchaseOrder = await _context.PurchaseOrders
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.PurchaseOrderId == purchaseOrderId);

            if (purchaseOrder is null)
            {
                throw new NotFoundException("Không tìm thấy phiếu nhập kho.");
            }

            EnsureNotCancelled(purchaseOrder);

            // Lock all affected variants before reading their stock/cost, just as
            // create/update do. A sale or return must wait until this transaction ends.
            var variants = await LoadVariantsAsync(purchaseOrder.Items.Select(x => x.VariantId));

            // Hàng của phiếu này có thể đã bán bớt: tồn kho phải đủ để trừ lại toàn bộ.
            var shortages = purchaseOrder.Items
                .GroupBy(x => variants.Single(v => v.VariantId == x.VariantId))
                .Where(g => g.Key.StockQuantity < g.Sum(x => (long)x.Quantity))
                .Select(g => $"{g.Key.SKU} (tồn {g.Key.StockQuantity}, cần trừ {g.Sum(x => (long)x.Quantity)})")
                .ToList();

            if (shortages.Count > 0)
            {
                throw new BadRequestException(
                    "Không thể hủy phiếu vì hàng đã được bán bớt, tồn kho không đủ để trừ lại: " +
                    string.Join(", ", shortages) + ".");
            }

            foreach (var item in purchaseOrder.Items)
            {
                ReverseStockIn(variants.Single(x => x.VariantId == item.VariantId), item.Quantity, item.UnitCost);
            }

            purchaseOrder.Status = PurchaseOrderStatuses.Cancelled;
            purchaseOrder.CancelledAt = DateTime.UtcNow;
            purchaseOrder.CancelReason = dto.Reason.Trim();

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return await GetByIdAsync(purchaseOrder.PurchaseOrderId);
        }

        private async Task<List<ProductVariant>> LoadVariantsAsync(IEnumerable<int> ids)
        {
            var variantIds = ids.Distinct().OrderBy(x => x).ToList();

            // SQL Server RCSI reads alone do not serialize a read/compute/write cycle.
            // Update locks conflict with both purchase writers and atomic order stock
            // updates; HOLDLOCK keeps them until commit/rollback. Always use ID order.
            if (_context.Database.IsSqlServer())
                foreach (var variantId in variantIds)
                    await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT VariantId FROM ProductVariants WITH (UPDLOCK, HOLDLOCK) WHERE VariantId = {variantId};");

            var previouslyTracked = _context.ChangeTracker.Entries<ProductVariant>()
                .Select(x => x.Entity.VariantId).ToHashSet();

            var variants = await _context.ProductVariants
                .Include(x => x.Product)
                .Include(x => x.Color)
                .Include(x => x.Size)
                .Where(x => variantIds.Contains(x.VariantId))
                .ToListAsync();

            if (variants.Count != variantIds.Count)
            {
                throw new NotFoundException("Một số biến thể sản phẩm không tồn tại.");
            }

            foreach (var variant in variants.Where(x => previouslyTracked.Contains(x.VariantId)))
                await _context.Entry(variant).ReloadAsync();

            if (variants.Any(x => x.StockQuantity < 0))
                throw new BadRequestException("Tồn kho hiện tại không hợp lệ. Vui lòng đối soát trước khi sửa phiếu nhập.");

            return variants;
        }

        private async Task LockPurchaseOrderAsync(int purchaseOrderId)
        {
            if (_context.Database.IsSqlServer())
                await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT PurchaseOrderId FROM PurchaseOrders WITH (UPDLOCK, HOLDLOCK) WHERE PurchaseOrderId = {purchaseOrderId};");
        }

        private static void ValidateItems(CreatePurchaseOrderDto dto)
        {
            if (dto.Items is null || dto.Items.Count == 0 || dto.Items.Any(x => x.Quantity <= 0 || x.UnitCost <= 0))
                throw new BadRequestException("Phiếu nhập phải có sản phẩm với số lượng và đơn giá lớn hơn 0.");
            if (dto.Items.GroupBy(x => x.VariantId).Any(g => g.Sum(x => (long)x.Quantity) > int.MaxValue))
                throw new BadRequestException("Tổng số lượng nhập của một biến thể vượt giới hạn.");
        }

        private static void AddItemsAndStockIn(
            PurchaseOrder purchaseOrder,
            IEnumerable<CreatePurchaseOrderItemDto> itemDtos,
            List<ProductVariant> variants)
        {
            decimal totalAmount = 0;

            foreach (var itemDto in itemDtos)
            {
                var variant = variants.First(x => x.VariantId == itemDto.VariantId);
                var lineTotal = itemDto.Quantity * itemDto.UnitCost;

                purchaseOrder.Items.Add(new PurchaseOrderItem
                {
                    VariantId = variant.VariantId,
                    ProductName = variant.Product.ProductName,
                    SKU = variant.SKU,
                    ColorName = variant.Color.ColorName,
                    SizeName = variant.Size.SizeName,
                    UnitCost = itemDto.UnitCost,
                    Quantity = itemDto.Quantity,
                    LineTotal = lineTotal
                });

                totalAmount += lineTotal;
                if (totalAmount > 9999999999999999.99m)
                    throw new BadRequestException("Tổng tiền phiếu nhập vượt giới hạn.");

                ApplyStockIn(variant, itemDto.Quantity, itemDto.UnitCost);
            }

            purchaseOrder.TotalAmount = totalAmount;
        }

        // Nhập kho: cộng dồn tồn kho và tính lại giá vốn bình quân gia quyền.
        // Giá vốn mới = (Tồn cũ × Giá vốn cũ + SL nhập × Giá nhập) / (Tồn cũ + SL nhập)
        private static void ApplyStockIn(ProductVariant variant, int quantity, decimal unitCost)
        {
            var newStock = (long)variant.StockQuantity + quantity;
            if (newStock > int.MaxValue)
                throw new BadRequestException($"Tồn kho của biến thể {variant.SKU} vượt giới hạn.");

            var oldStock = Math.Max(variant.StockQuantity, 0);
            var oldCost = variant.AverageCostPrice ?? unitCost;

            variant.AverageCostPrice = Math.Round(
                (oldStock * oldCost + quantity * unitCost) / ((long)oldStock + quantity),
                2);

            variant.StockQuantity = (int)newStock;
        }

        // Hoàn tác nhập kho: trừ tồn kho và bỏ phần giá trị của lô hàng này khỏi giá vốn.
        // Giá vốn mới = (Tồn hiện tại × Giá vốn hiện tại − SL nhập × Giá nhập) / (Tồn hiện tại − SL nhập)
        // Khi tồn còn lại bằng 0, giữ nguyên giá vốn: lần nhập sau sẽ lấy theo giá nhập mới.
        private static void ReverseStockIn(ProductVariant variant, int quantity, decimal unitCost)
        {
            var remainingStock = variant.StockQuantity - quantity;

            if (remainingStock > 0 && variant.AverageCostPrice.HasValue)
            {
                var remainingValue = variant.StockQuantity * variant.AverageCostPrice.Value - quantity * unitCost;

                if (remainingValue > 0)
                {
                    variant.AverageCostPrice = Math.Round(remainingValue / remainingStock, 2);
                }
            }

            variant.StockQuantity = remainingStock;
        }

        private static void EnsureNotCancelled(PurchaseOrder purchaseOrder)
        {
            if (purchaseOrder.Status == PurchaseOrderStatuses.Cancelled)
            {
                throw new BadRequestException("Phiếu nhập kho đã bị hủy, không thể thao tác thêm.");
            }
        }

        private async Task<string> GeneratePurchaseOrderCodeAsync()
        {
            string purchaseOrderCode;

            do
            {
                purchaseOrderCode = $"PN{VietnamTime.Now:yyyyMMddHHmmssfff}{Random.Shared.Next(100, 1000)}";
            }
            while (await _context.PurchaseOrders.AnyAsync(x => x.PurchaseOrderCode == purchaseOrderCode));

            return purchaseOrderCode;
        }

        private static PurchaseOrderDetailDto MapToDetailDto(PurchaseOrder purchaseOrder)
        {
            return new PurchaseOrderDetailDto
            {
                PurchaseOrderId = purchaseOrder.PurchaseOrderId,
                PurchaseOrderCode = purchaseOrder.PurchaseOrderCode,
                SupplierId = purchaseOrder.SupplierId,
                SupplierName = purchaseOrder.Supplier.SupplierName,
                SupplierPhone = purchaseOrder.Supplier.Phone,
                CreatedByUserName = purchaseOrder.CreatedByUser.FullName,
                Note = purchaseOrder.Note,
                TotalAmount = purchaseOrder.TotalAmount,
                Status = purchaseOrder.Status,
                CreatedAt = purchaseOrder.CreatedAt,
                UpdatedAt = purchaseOrder.UpdatedAt,
                CancelledAt = purchaseOrder.CancelledAt,
                CancelReason = purchaseOrder.CancelReason,
                Items = purchaseOrder.Items.Select(x => new PurchaseOrderItemDto
                {
                    PurchaseOrderItemId = x.PurchaseOrderItemId,
                    VariantId = x.VariantId,
                    ProductName = x.ProductName,
                    SKU = x.SKU,
                    ColorName = x.ColorName,
                    SizeName = x.SizeName,
                    UnitCost = x.UnitCost,
                    Quantity = x.Quantity,
                    LineTotal = x.LineTotal
                }).ToList()
            };
        }
    }
}
