using Inventory.Common.DTOs;
using Inventory.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.DAL.Repositories;

public sealed class IngredientRepository(InventoryDbContext db)
{
    public Task<Ingredient?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return db.Ingredients.SingleOrDefaultAsync(x => x.IngredientId == id, ct);
    }

    public async Task CreateAsync(Ingredient ingredient, CancellationToken ct)
    {
        await db.Ingredients.AddAsync(ingredient, ct);
    }

    public async Task<Dictionary<Guid, decimal>> GetIssuableQuantitiesAsync(
        IReadOnlyCollection<Guid> ingredientIds,
        DateOnly today,
        CancellationToken ct)
    {
        if (ingredientIds.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var rows = await db.GoodsReceiptItems.AsNoTracking()
            .Where(x => ingredientIds.Contains(x.IngredientId)
                && x.GoodsReceipt.Status == "Confirmed"
                && x.ExpiryDate.HasValue
                && x.ExpiryDate.Value >= today)
            .Select(x => new
            {
                x.IngredientId,
                x.ReceivedQuantity,
                Changes = x.StockMovements
                    .Where(m => m.MovementType != "Receipt")
                    .Select(m => m.QuantityChange)
                    .ToList()
            })
            .ToListAsync(ct);

        var totals = new Dictionary<Guid, decimal>();
        foreach (var row in rows)
        {
            var remaining = row.ReceivedQuantity + row.Changes.Sum();
            if (remaining <= 0)
            {
                continue;
            }

            totals[row.IngredientId] = totals.GetValueOrDefault(row.IngredientId) + remaining;
        }

        return totals;
    }

    public async Task<PageDTO<IngredientSummaryDTO>> GetAllAsync(
        int page,
        int pageSize,
        string? search,
        bool? active,
        bool lowStock,
        DateOnly today,
        CancellationToken ct)
    {
        var query = db.Ingredients.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(x => x.IngredientName.Contains(search) || x.IngredientCode.Contains(search));
        }

        if (active.HasValue)
        {
            query = query.Where(x => x.IsActive == active.Value);
        }

        var orderedQuery = query
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.IngredientId);

        if (lowStock)
        {
            var candidates = await orderedQuery
                .Where(x => x.IsActive)
                .ToListAsync(ct);
            var issuable = await GetIssuableQuantitiesAsync(
                candidates.Select(x => x.IngredientId).ToArray(),
                today,
                ct);
            var matched = candidates
                .Where(x => issuable.GetValueOrDefault(x.IngredientId) <= x.LowStockThreshold)
                .ToList();
            var items = matched
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => MapSummary(x, true))
                .ToList();
            return new PageDTO<IngredientSummaryDTO>
            {
                Items = items,
                TotalCount = matched.Count,
                Page = page,
                PageSize = pageSize
            };
        }

        var totalCount = await query.CountAsync(ct);
        var pageEntities = await orderedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        var activeIds = pageEntities
            .Where(x => x.IsActive)
            .Select(x => x.IngredientId)
            .ToArray();
        var pageIssuable = await GetIssuableQuantitiesAsync(activeIds, today, ct);
        var pageItems = pageEntities
            .Select(x => MapSummary(x, x.IsActive && pageIssuable.GetValueOrDefault(x.IngredientId) <= x.LowStockThreshold))
            .ToList();
        return new PageDTO<IngredientSummaryDTO>
        {
            Items = pageItems,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    private static IngredientSummaryDTO MapSummary(Ingredient value, bool isLowStock)
    {
        return new IngredientSummaryDTO
        {
            IngredientId = value.IngredientId,
            IngredientCode = value.IngredientCode,
            IngredientName = value.IngredientName,
            BaseUnit = value.BaseUnit,
            StockQuantity = value.StockQuantity,
            LowStockThreshold = value.LowStockThreshold,
            IsActive = value.IsActive,
            IsLowStock = isLowStock,
            StockVersion = value.StockVersion
        };
    }
}
