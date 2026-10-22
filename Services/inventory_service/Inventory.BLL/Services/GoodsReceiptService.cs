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
                    throw new InventoryException(409, "Receipt ID was already used with different data or by another account.");
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
                    throw new InventoryException(400, "Converted quantity exceeds the supported range.");
                }
                if (quantity <= 0 || !InventoryValidation.Quantity(quantity))
                {
                    throw new InventoryException(400, "Converted quantity must fit decimal(18,6) without rounding.");
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
                throw new InventoryException(409, "Only a non-empty draft receipt can be confirmed.");
            }
            foreach (var line in receipt.GoodsReceiptItems.OrderBy(x => x.IngredientId))
            {
                if (line.ExpiryDate is null || line.ReceivedQuantity <= 0 || !InventoryValidation.Quantity(line.ReceivedQuantity))
                {
                    throw new InventoryException(409, "Receipt contains invalid quantity or missing expiry date.");
                }
                var ingredient = await ActiveIngredient(line.IngredientId, ct);
                var newQuantity = ingredient.StockQuantity + line.ReceivedQuantity;
                if (!InventoryValidation.Quantity(newQuantity))
                {
                    throw new InventoryException(409, "Stock quantity exceeds the supported range.");
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
        return await repository.GetByIdAsync(id, ct) ?? throw new InventoryException(404, "Receipt not found.");
    }

    private async Task<Ingredient> ActiveIngredient(Guid id, CancellationToken ct)
    {
        var ingredient = await ingredients.GetByIdAsync(id, ct)
            ?? throw new InventoryException(404, "Ingredient not found.");
        if (!ingredient.IsActive)
        {
            throw new InventoryException(409, "Cannot receive an inactive ingredient.");
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
            throw new InventoryException(400, "Provide 1-100 unique ingredient/expiry lines with positive quantities (up to six decimal places), and a note of at most 1000 characters.");
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
