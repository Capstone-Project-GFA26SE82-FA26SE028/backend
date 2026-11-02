using Inventory.BLL.Interfaces;
using Inventory.Common;
using Inventory.Common.DTOs;
using Inventory.DAL;
using Inventory.DAL.Models;
using Inventory.DAL.Repositories;

namespace Inventory.BLL.Services;

public sealed class IngredientService(IngredientRepository repository, InventoryTransaction transaction, IInventoryAccess access, UnitRepository units)
{
    public async Task<Ingredient> Create(IngredientWriteDTO request, CancellationToken ct)
    {
        await access.RequireAsync("ManageIngredients", ct);
        var (name, unit) = Validate(request);
        return await transaction.ExecuteAsync(async () =>
        {
            if (await units.GetByCodeAsync(unit, ct) is null)
            {
                throw new InventoryException(400, "Vui lòng tạo đơn vị tính cơ sở trước khi tạo nguyên liệu.");
            }
            var id = Guid.NewGuid();
            var ingredient = new Ingredient
            {
                IngredientId = id,
                IngredientCode = $"ING-{id:N}",
                IngredientName = name,
                BaseUnit = unit,
                LowStockThreshold = request.LowStockThreshold,
                StockQuantity = 0,
                IsActive = true
            };
            await repository.CreateAsync(ingredient, ct);
            return ingredient;
        }, ct);
    }

    public async Task<Ingredient> Update(Guid id, IngredientWriteDTO request, CancellationToken ct)
    {
        await access.RequireAsync("ManageIngredients", ct);
        var (name, unit) = Validate(request);
        return await transaction.ExecuteAsync(async () =>
        {
            var ingredient = await Find(id, ct);
            if (!string.Equals(ingredient.BaseUnit, unit, StringComparison.OrdinalIgnoreCase))
            {
                throw new InventoryException(409, "Đơn vị tính cơ sở không thể thay đổi vì các công thức có thể đang tham chiếu. Hãy tạo nguyên liệu mới.");
            }
            ingredient.IngredientName = name;
            ingredient.LowStockThreshold = request.LowStockThreshold;
            return ingredient;
        }, ct);
    }

    public async Task<Ingredient> SetStatus(Guid id, bool active, CancellationToken ct)
    {
        await access.RequireAsync("ManageIngredients", ct);
        return await transaction.ExecuteAsync(async () =>
        {
            var ingredient = await Find(id, ct);
            ingredient.IsActive = active;
            return ingredient;
        }, ct);
    }

    public async Task<Ingredient> GetById(Guid id, CancellationToken ct)
    {
        await access.RequireAsync("Read", ct);
        return await Find(id, ct);
    }

    public async Task<PageDTO<Ingredient>> GetAll(int page, int pageSize, string? search, bool? active, bool lowStock, CancellationToken ct)
    {
        await access.RequireAsync("Read", ct);
        InventoryValidation.Page(page, pageSize);
        if (search?.Length > 255)
        {
            throw new InventoryException(400, "Từ khóa tìm kiếm quá dài.");
        }
        return await repository.GetAllAsync(page, pageSize, search?.Trim(), active, lowStock, ct);
    }

    private async Task<Ingredient> Find(Guid id, CancellationToken ct)
    {
        InventoryValidation.Id(id);
        return await repository.GetByIdAsync(id, ct) ?? throw new InventoryException(404, "Không tìm thấy nguyên liệu.");
    }

    private static (string Name, string Unit) Validate(IngredientWriteDTO request)
    {
        var name = request.IngredientName?.Trim() ?? "";
        var unit = request.BaseUnit?.Trim().ToLowerInvariant() ?? "";
        if (name.Length is < 1 or > 255 || name.Any(char.IsControl)
            || unit.Length is < 1 or > 50 || unit.Any(char.IsControl)
            || !InventoryValidation.Quantity(request.LowStockThreshold))
        {
            throw new InventoryException(400, "Tên, đơn vị tính cơ sở và ngưỡng tồn kho không âm tối đa 6 chữ số thập phân là bắt buộc.");
        }
        return (name, unit);
    }
}
