using Inventory.BLL.Interfaces;
using Inventory.Common;
using Inventory.DAL;
using Inventory.DAL.Models;
using Inventory.DAL.Repositories;

namespace Inventory.BLL.Services;

public sealed class IngredientUnitService(IngredientRepository ingredients, UnitRepository units,
    IngredientUnitRepository repository, InventoryTransaction transaction, IInventoryAccess access)
{
    public async Task<IngredientUnit> Update(Guid ingredientId, Guid unitId, decimal factor, CancellationToken ct)
    {
        await access.RequireAsync("ManageIngredients", ct);
        if (factor <= 0 || !InventoryValidation.Quantity(factor))
        {
            throw new InventoryException(400, "Hệ số quy đổi phải là số dương có tối đa 6 chữ số thập phân.");
        }
        return await transaction.ExecuteAsync(async () =>
        {
            var ingredient = await Find(ingredientId, ct);
            var unit = await units.GetByIdAsync(unitId, ct) ?? throw new InventoryException(404, "Không tìm thấy đơn vị tính.");
            if (string.Equals(unit.Code, ingredient.BaseUnit, StringComparison.OrdinalIgnoreCase) && factor != 1)
            {
                throw new InventoryException(409, "Hệ số quy đổi của đơn vị tính cơ sở phải bằng 1.");
            }
            var conversion = await repository.GetByIdAsync(ingredientId, unitId, ct);
            if (conversion is null)
            {
                conversion = new IngredientUnit
                {
                    IngredientId = ingredientId,
                    UnitId = unitId,
                    ConversionFactor = factor
                };
                await repository.CreateAsync(conversion, ct);
            }
            else
            {
                conversion.ConversionFactor = factor;
            }
            return conversion;
        }, ct);
    }

    public async Task<List<IngredientUnit>> GetAll(Guid ingredientId, CancellationToken ct)
    {
        await access.RequireAsync("Read", ct);
        var ingredient = await Find(ingredientId, ct);
        var result = await repository.GetAllAsync(ingredientId, ct);
        var baseUnit = await units.GetByCodeAsync(ingredient.BaseUnit.ToLowerInvariant(), ct);
        if (baseUnit is not null && result.All(x => x.UnitId != baseUnit.UnitId))
        {
            result.Add(new IngredientUnit
            {
                IngredientId = ingredientId,
                UnitId = baseUnit.UnitId,
                ConversionFactor = 1
            });
        }
        return result;
    }

    public async Task<decimal> Resolve(Guid ingredientId, Guid unitId, CancellationToken ct)
    {
        await access.RequireAsync("Read", ct);
        return await ResolveAsync(ingredientId, unitId, ct);
    }

    // Used inside receipt transactions after the caller's permission check.
    public async Task<decimal> ResolveAsync(Guid ingredientId, Guid unitId, CancellationToken ct)
    {
        var ingredient = await Find(ingredientId, ct);
        if (!ingredient.IsActive)
        {
            throw new InventoryException(409, "Nguyên liệu đang ngừng hoạt động.");
        }
        var unit = await units.GetByIdAsync(unitId, ct) ?? throw new InventoryException(404, "Không tìm thấy đơn vị tính.");
        if (string.Equals(unit.Code, ingredient.BaseUnit, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }
        var conversion = await repository.GetByIdAsync(ingredientId, unitId, ct)
            ?? throw new InventoryException(409, "Đơn vị tính chưa có tỷ lệ quy đổi cho nguyên liệu này.");
        return conversion.ConversionFactor;
    }

    private async Task<Ingredient> Find(Guid id, CancellationToken ct)
    {
        InventoryValidation.Id(id);
        return await ingredients.GetByIdAsync(id, ct) ?? throw new InventoryException(404, "Không tìm thấy nguyên liệu.");
    }
}
