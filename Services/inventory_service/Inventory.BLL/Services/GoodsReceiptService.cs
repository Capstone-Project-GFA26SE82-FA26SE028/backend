using Inventory.BLL.Interfaces;
using Inventory.Common;
using Inventory.Common.DTOs;
using Inventory.DAL;
using Inventory.DAL.Models;
using Inventory.DAL.Repositories;

namespace Inventory.BLL.Services;

public sealed class GoodsReceiptService(GoodsReceiptRepository repository, IngredientRepository ingredients,
    StockMovementRepository movements, InventoryTransaction transaction, IInventoryAccess access, TimeProvider clock, IngredientUnitService conversions)
{
    public async Task<GoodsReceipt> Create(ReceiptWriteDTO request, CancellationToken ct)
    {
        var actor = await access.RequireAsync("Receive", ct);
        Validate(request);
        return await transaction.ExecuteAsync(async () =>
        {
            var existing = await repository.GetByIdAsync(request.GoodsReceiptId, ct);
            if (existing is not null)
            {
                if (existing.CreatedByRef != actor || !Same(existing, request))
                {
                    throw new InventoryException(409, "Mã phiếu nhập đã được dùng với dữ liệu khác hoặc bởi tài khoản khác.");
                }
                return existing;
            }
            foreach (var line in request.Items)
            {
                await ActiveIngredient(line.IngredientId, ct);
            }
            var lines = new List<GoodsReceiptItem>();
            foreach (var line in request.Items)
            {
                var factor = line.UnitId.HasValue
                    ? await conversions.ResolveAsync(line.IngredientId, line.UnitId.Value, ct) : 1m;
                decimal quantity;
                try
                {
                    quantity = checked(line.ReceivedQuantity * factor);
                }
                catch (OverflowException)
                {
                    throw new InventoryException(400, "Số lượng sau quy đổi vượt quá phạm vi hỗ trợ.");
                }
                if (quantity <= 0 || !InventoryValidation.Quantity(quantity))
                {
                    throw new InventoryException(400, "Số lượng sau quy đổi phải theo định dạng decimal(18,6) không làm tròn.");
                }
                lines.Add(new GoodsReceiptItem
                {
                    GoodsReceiptItemId = Guid.NewGuid(),
                    GoodsReceiptId = request.GoodsReceiptId,
                    IngredientId = line.IngredientId,
                    ReceivedQuantity = quantity,
                    InputQuantity = line.ReceivedQuantity,
                    InputUnitId = line.UnitId,
                    ConversionFactorSnapshot = factor,
                    ExpiryDate = line.ExpiryDate
                });
            }
            var receipt = new GoodsReceipt
            {
                GoodsReceiptId = request.GoodsReceiptId,
                ReceiptCode = $"GR-{request.GoodsReceiptId:N}",
                Status = "Draft",
                CreatedByRef = actor,
                CreatedAt = clock.GetUtcNow().UtcDateTime,
                Note = request.Note?.Trim(),
                GoodsReceiptItems = lines
            };
            await repository.CreateAsync(receipt, ct);
            return receipt;
        }, ct);
    }

    public async Task<GoodsReceipt> Confirm(Guid id, CancellationToken ct)
    {
        var actor = await access.RequireAsync("Receive", ct);
        return await transaction.ExecuteAsync(async () =>
        {
            var receipt = await Find(id, ct);
            if (receipt.Status == "Confirmed")
            {
                return receipt;
            }
            if (receipt.Status != "Draft" || receipt.GoodsReceiptItems.Count == 0)
            {
                throw new InventoryException(409, "Chỉ phiếu nhập nháp có hàng mới có thể xác nhận.");
            }
            foreach (var line in receipt.GoodsReceiptItems.OrderBy(x => x.IngredientId))
            {
                if (line.ExpiryDate is null || line.ReceivedQuantity <= 0 || !InventoryValidation.Quantity(line.ReceivedQuantity))
                {
                    throw new InventoryException(409, "Phiếu nhập chứa số lượng không hợp lệ hoặc thiếu ngày hết hạn.");
                }
                var ingredient = await ActiveIngredient(line.IngredientId, ct);
                var newQuantity = ingredient.StockQuantity + line.ReceivedQuantity;
                if (!InventoryValidation.Quantity(newQuantity))
                {
                    throw new InventoryException(409, "Số lượng tồn kho vượt quá phạm vi hỗ trợ.");
                }
                ingredient.StockQuantity = newQuantity;
                await movements.CreateAsync(new StockMovement
                {
                    StockMovementId = Guid.NewGuid(),
                    IngredientId = ingredient.IngredientId,
                    GoodsReceiptItemId = line.GoodsReceiptItemId,
                    MovementType = "Receipt",
                    QuantityChange = line.ReceivedQuantity,
                    PerformedByRef = actor,
                    Reason = receipt.ReceiptCode,
                    CreatedAt = clock.GetUtcNow().UtcDateTime
                }, ct);
            }
            receipt.Status = "Confirmed";
            receipt.ConfirmAt = clock.GetUtcNow().UtcDateTime;
            return receipt;
        }, ct);
    }

    public async Task<GoodsReceipt> GetById(Guid id, CancellationToken ct)
    {
        await access.RequireAsync("Read", ct);
        return await Find(id, ct);
    }

    public async Task<PageDTO<GoodsReceipt>> GetAll(int page, int pageSize, CancellationToken ct)
    {
        await access.RequireAsync("Read", ct);
        InventoryValidation.Page(page, pageSize);
        return await repository.GetAllAsync(page, pageSize, ct);
    }

    private async Task<GoodsReceipt> Find(Guid id, CancellationToken ct)
    {
        InventoryValidation.Id(id);
        return await repository.GetByIdAsync(id, ct) ?? throw new InventoryException(404, "Không tìm thấy phiếu nhập kho.");
    }

    private async Task<Ingredient> ActiveIngredient(Guid id, CancellationToken ct)
    {
        var ingredient = await ingredients.GetByIdAsync(id, ct)
            ?? throw new InventoryException(404, "Không tìm thấy nguyên liệu.");
        if (!ingredient.IsActive)
        {
            throw new InventoryException(409, "Không thể nhập kho nguyên liệu đang ngừng hoạt động.");
        }
        return ingredient;
    }

    private static void Validate(ReceiptWriteDTO request)
    {
        InventoryValidation.Id(request.GoodsReceiptId);
        if (request.Note?.Length > 1000 || request.Items is null || request.Items.Count is < 1 or > 100
            || request.Items.Any(x => x is null || x.IngredientId == Guid.Empty || x.ExpiryDate is null
                || x.ReceivedQuantity <= 0 || !InventoryValidation.Quantity(x.ReceivedQuantity))
            || request.Items.Select(x => (x.IngredientId, x.ExpiryDate)).Distinct().Count() != request.Items.Count)
        {
            throw new InventoryException(400, "Cung cấp từ 1-100 dòng nguyên liệu/hạn dùng không trùng lặp với số lượng dương (tối đa 6 chữ số thập phân), ghi chú tối đa 1000 ký tự.");
        }
    }

    private static bool Same(GoodsReceipt receipt, ReceiptWriteDTO request)
    {
        return (receipt.Note ?? "") == (request.Note?.Trim() ?? "")
            && receipt.GoodsReceiptItems.Count == request.Items.Count
            && request.Items.All(line => receipt.GoodsReceiptItems.Any(x => x.IngredientId == line.IngredientId
                && (x.InputQuantity ?? x.ReceivedQuantity) == line.ReceivedQuantity
                && x.InputUnitId == line.UnitId && x.ExpiryDate == line.ExpiryDate));
    }
}
