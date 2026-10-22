using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Inventory.BLL.Interfaces;
using Inventory.Common;
using Inventory.Common.DTOs;
using Inventory.DAL;
using Inventory.DAL.Models;
using Inventory.DAL.Repositories;

namespace Inventory.BLL.Services;

public sealed class StockIssueService(StockIssueRepository repository, StockLotRepository lots,
    IngredientRepository ingredients, InventoryTransaction transaction, IInventoryAccess access, TimeProvider clock)
{
    public async Task<StockIssue> Create(StockIssueWriteDTO request, CancellationToken ct)
    {
        var actor = await access.RequireAsync("Issue", ct);
        InventoryValidation.Id(request.StockIssueId);
        var reason = request.Reason?.Trim() ?? "";
        if (reason.Length is < 1 or > 1000 || reason.Any(char.IsControl)
            || request.Items is null || request.Items.Count is < 1 or > 100
            || request.Items.Any(x => x is null || x.IngredientId == Guid.Empty || x.Quantity <= 0 || !InventoryValidation.Quantity(x.Quantity))
            || request.Items.Select(x => x.IngredientId).Distinct().Count() != request.Items.Count)
        {
            throw new InventoryException(400, "Provide a reason and 1-100 unique ingredients with positive base-unit quantities (maximum six decimal places).");
        }
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            Reason = reason,
            Items = request.Items.OrderBy(x => x.IngredientId).Select(x => new
            {
                x.IngredientId,
                Quantity = x.Quantity.ToString("0.######", CultureInfo.InvariantCulture)
            }).ToList()
        }))));
        return await transaction.ExecuteAsync(async () =>
        {
            var existing = await repository.GetByIdAsync(request.StockIssueId, ct);
            if (existing is not null)
            {
                if (existing.RequestHash != hash || existing.CreatedByRef != actor)
                {
                    throw new InventoryException(409, "Issue ID already used with different data or by another account.");
                }
                return existing;
            }
            var issue = new StockIssue
            {
                StockIssueId = request.StockIssueId,
                CreatedByRef = actor,
                CreatedAt = clock.GetUtcNow().UtcDateTime,
                Reason = reason,
                RequestHash = hash
            };
            var today = StockLotService.BusinessDate(clock);
            foreach (var line in request.Items.OrderBy(x => x.IngredientId))
            {
                var ingredient = await ingredients.GetByIdAsync(line.IngredientId, ct)
                    ?? throw new InventoryException(404, "Ingredient not found.");
                if (!ingredient.IsActive || ingredient.StockQuantity < line.Quantity)
                {
                    throw new InventoryException(409, "Ingredient is inactive or has insufficient stock.");
                }
                var available = await lots.GetAllAsync(line.IngredientId, ct);
                if (available.Any(x => x.RemainingQuantity < 0))
                {
                    throw new InventoryException(409, "Lot balance is inconsistent. Reconcile inventory before issuing.");
                }
                var remaining = line.Quantity;
                foreach (var lot in available.Where(x => x.ExpiryDate >= today && x.RemainingQuantity > 0))
                {
                    var quantity = Math.Min(remaining, lot.RemainingQuantity);
                    issue.Movements.Add(new StockMovement
                    {
                        StockMovementId = Guid.NewGuid(),
                        StockIssueId = issue.StockIssueId,
                        IngredientId = line.IngredientId,
                        GoodsReceiptItemId = lot.GoodsReceiptItemId,
                        MovementType = "Issue",
                        QuantityChange = -quantity,
                        PerformedByRef = actor,
                        Reason = reason,
                        CreatedAt = issue.CreatedAt
                    });
                    remaining -= quantity;
                    if (remaining == 0)
                    {
                        break;
                    }
                }
                if (remaining > 0)
                {
                    throw new InventoryException(409, "Insufficient unexpired lot stock. No inventory was issued.");
                }
                ingredient.StockQuantity -= line.Quantity;
            }
            await repository.CreateAsync(issue, ct);
            return issue;
        }, ct);
    }

    public async Task<StockIssue> GetById(Guid id, CancellationToken ct)
    {
        await access.RequireAsync("Read", ct);
        InventoryValidation.Id(id);
        return await repository.GetByIdAsync(id, ct) ?? throw new InventoryException(404, "Stock issue not found.");
    }
}
