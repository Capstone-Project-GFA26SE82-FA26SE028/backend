using Inventory.Common.DTOs;
using Inventory.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.DAL.Repositories;

public sealed class StockLotRepository(InventoryDbContext db)
{
    public async Task<List<StockLotDTO>> GetAllAsync(Guid ingredientId, CancellationToken ct)
    {
        var rows = await db.GoodsReceiptItems.AsNoTracking()
            .Where(x => x.IngredientId == ingredientId && x.GoodsReceipt.Status == "Confirmed")
            .Select(x => new
            {
                x.GoodsReceiptItemId,
                x.IngredientId,
                x.ExpiryDate,
                x.ReceivedQuantity,
                ReceivedAt = x.GoodsReceipt.ConfirmAt ?? x.GoodsReceipt.CreatedAt,
                Changes = x.StockMovements.Where(m => m.MovementType != "Receipt").Select(m => m.QuantityChange).ToList()
            }).ToListAsync(ct);
        return rows.Select(x => new StockLotDTO
        {
            GoodsReceiptItemId = x.GoodsReceiptItemId,
            IngredientId = x.IngredientId,
            ExpiryDate = x.ExpiryDate,
            ReceivedAt = x.ReceivedAt,
            ReceivedQuantity = x.ReceivedQuantity,
            RemainingQuantity = x.ReceivedQuantity + x.Changes.Sum()
        }).OrderBy(x => x.ReceivedAt).ThenBy(x => x.GoodsReceiptItemId).ToList();
    }
}
