using Inventory.BLL.Interfaces;
using Inventory.Common;
using Inventory.Common.DTOs;
using Inventory.DAL;
using Inventory.DAL.Repositories;

namespace Inventory.BLL.Services;

public sealed class StockLotService(StockLotRepository repository, IngredientRepository ingredients,
    IInventoryAccess access, InventoryTransaction transaction, TimeProvider clock)
{
    public async Task<PageDTO<StockLotDTO>> GetAll(Guid ingredientId, int page, int pageSize, CancellationToken ct)
    {
        await access.RequireAsync("Read", ct);
        InventoryValidation.Id(ingredientId);
        InventoryValidation.Page(page, pageSize);
        return await transaction.ExecuteAsync(async () =>
        {
            var ingredient = await ingredients.GetByIdAsync(ingredientId, ct)
                ?? throw new InventoryException(404, "Không tìm thấy nguyên liệu.");
            var lots = await repository.GetAllAsync(ingredientId, ct);
            var today = BusinessDate(clock);
            foreach (var lot in lots)
            {
                lot.ExpiryStatus = lot.ExpiryDate is null ? "Unknown"
                    : lot.ExpiryDate < today ? "Expired"
                    : lot.ExpiryDate.Value.DayNumber - today.DayNumber <= 7 ? "ExpiringSoon" : "Valid";
                lot.CanIssue = ingredient.IsActive && lot.RemainingQuantity > 0 && lot.ExpiryDate >= today;
            }
            return new PageDTO<StockLotDTO>
            {
                Items = lots.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
                TotalCount = lots.Count,
                Page = page,
                PageSize = pageSize
            };
        }, ct);
    }

    public static DateOnly BusinessDate(TimeProvider clock)
    {
        return DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime);
    }
}
