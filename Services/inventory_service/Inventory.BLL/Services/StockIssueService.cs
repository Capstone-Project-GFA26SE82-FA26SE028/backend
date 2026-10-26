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

    public async Task<CookingDeductionResultDTO> DeductForCooking(CookingDeductionDTO request, CancellationToken ct)
    {
        var actor = await access.RequireAsync("Cooking", ct);
        InventoryValidation.Id(request.CookingEventId);

        if (request.Items is null || request.Items.Count == 0)
        {
            return new CookingDeductionResultDTO
            {
                StockIssueId = request.CookingEventId,
                Movements = [],
                LowStockAlerts = []
            };
        }

        var hasIngredients = request.Items.Any(i => i.Ingredients is not null && i.Ingredients.Count > 0);
        if (!hasIngredients)
        {
            return new CookingDeductionResultDTO
            {
                StockIssueId = request.CookingEventId,
                Movements = [],
                LowStockAlerts = []
            };
        }

        foreach (var item in request.Items)
        {
            InventoryValidation.Id(item.TaskId);
            InventoryValidation.Id(item.OrderItemId);
            if (item.Quantity <= 0)
            {
                throw new InventoryException(400, "Item portion quantity must be positive.");
            }

            if (item.Ingredients is not null)
            {
                foreach (var line in item.Ingredients)
                {
                    if (line.IngredientId == Guid.Empty || line.Quantity <= 0 || !InventoryValidation.Quantity(line.Quantity))
                    {
                        throw new InventoryException(400, "Provide valid ingredient IDs and positive quantities.");
                    }
                }
            }
        }

        return await transaction.ExecuteAsync(async () =>
        {
            var existing = await repository.GetByIdAsync(request.CookingEventId, ct);
            if (existing is not null)
            {
                return new CookingDeductionResultDTO
                {
                    StockIssueId = existing.StockIssueId,
                    Movements = existing.Movements.Select(MapMovement).ToList(),
                    LowStockAlerts = []
                };
            }

            var totalNeededPerIngredient = new Dictionary<Guid, decimal>();
            foreach (var item in request.Items)
            {
                if (item.Ingredients is null)
                {
                    continue;
                }

                foreach (var line in item.Ingredients)
                {
                    if (totalNeededPerIngredient.TryGetValue(line.IngredientId, out var current))
                    {
                        totalNeededPerIngredient[line.IngredientId] = current + line.Quantity;
                    }
                    else
                    {
                        totalNeededPerIngredient[line.IngredientId] = line.Quantity;
                    }
                }
            }

            var today = StockLotService.BusinessDate(clock);
            var ingredientEntities = new Dictionary<Guid, Ingredient>();
            var availableLotsPerIngredient = new Dictionary<Guid, List<StockLotDTO>>();

            foreach (var (ingredientId, totalNeeded) in totalNeededPerIngredient)
            {
                var ingredient = await ingredients.GetByIdAsync(ingredientId, ct)
                    ?? throw new InventoryException(404, $"Ingredient '{ingredientId}' not found.");

                if (!ingredient.IsActive)
                {
                    throw new InventoryException(409, $"Nguyên liệu '{ingredient.IngredientName}' đang ngừng hoạt động.");
                }

                if (ingredient.StockQuantity < totalNeeded)
                {
                    throw new InventoryException(409, $"Nguyên liệu '{ingredient.IngredientName}' không đủ tồn kho (cần: {totalNeeded:0.######}, hiện có: {ingredient.StockQuantity:0.######}).");
                }

                var available = await lots.GetAllAsync(ingredientId, ct);
                if (available.Any(x => x.RemainingQuantity < 0))
                {
                    throw new InventoryException(409, "Lot balance is inconsistent. Reconcile inventory before issuing.");
                }

                var unexpiredTotal = available
                    .Where(x => x.ExpiryDate >= today && x.RemainingQuantity > 0)
                    .Sum(x => x.RemainingQuantity);

                if (unexpiredTotal < totalNeeded)
                {
                    throw new InventoryException(409, $"Nguyên liệu '{ingredient.IngredientName}' không đủ tồn kho khả dụng chưa hết hạn (cần: {totalNeeded:0.######}, khả dụng: {unexpiredTotal:0.######}).");
                }

                ingredientEntities[ingredientId] = ingredient;
                availableLotsPerIngredient[ingredientId] = available
                    .Where(x => x.ExpiryDate >= today && x.RemainingQuantity > 0)
                    .Select(x => new StockLotDTO
                    {
                        GoodsReceiptItemId = x.GoodsReceiptItemId,
                        IngredientId = x.IngredientId,
                        ExpiryDate = x.ExpiryDate,
                        ReceivedAt = x.ReceivedAt,
                        ReceivedQuantity = x.ReceivedQuantity,
                        RemainingQuantity = x.RemainingQuantity
                    }).ToList();
            }

            var reason = request.Items.Count == 1
                ? $"Chế biến món: {request.Items[0].DishName} (Vé: {request.Items[0].TaskId})"
                : $"Chế biến {request.Items.Count} món (Sự kiện: {request.CookingEventId})";

            var issue = new StockIssue
            {
                StockIssueId = request.CookingEventId,
                CreatedByRef = actor,
                CreatedAt = clock.GetUtcNow().UtcDateTime,
                Reason = reason,
                RequestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.CookingEventId.ToString())))
            };

            foreach (var item in request.Items)
            {
                if (item.Ingredients is null)
                {
                    continue;
                }

                var itemReason = string.IsNullOrWhiteSpace(item.DishName)
                    ? $"Chế biến món (Vé: {item.TaskId})"
                    : $"Chế biến món: {item.DishName} (Vé: {item.TaskId})";

                foreach (var line in item.Ingredients)
                {
                    var lotList = availableLotsPerIngredient[line.IngredientId];
                    var remaining = line.Quantity;

                    foreach (var lot in lotList.Where(x => x.RemainingQuantity > 0))
                    {
                        var quantity = Math.Min(remaining, lot.RemainingQuantity);
                        issue.Movements.Add(new StockMovement
                        {
                            StockMovementId = Guid.NewGuid(),
                            StockIssueId = issue.StockIssueId,
                            IngredientId = line.IngredientId,
                            GoodsReceiptItemId = lot.GoodsReceiptItemId,
                            MovementType = "Cooking",
                            QuantityChange = -quantity,
                            CookingEventRef = request.CookingEventId,
                            OrderItemRef = item.OrderItemId,
                            PerformedByRef = actor,
                            Reason = itemReason,
                            CreatedAt = issue.CreatedAt
                        });

                        lot.RemainingQuantity -= quantity;
                        remaining -= quantity;
                        if (remaining == 0)
                        {
                            break;
                        }
                    }
                }
            }

            var lowStockAlerts = new List<LowStockAlertDTO>();
            foreach (var (ingredientId, totalNeeded) in totalNeededPerIngredient)
            {
                var ingredient = ingredientEntities[ingredientId];
                ingredient.StockQuantity -= totalNeeded;

                if (ingredient.StockQuantity <= ingredient.LowStockThreshold)
                {
                    lowStockAlerts.Add(new LowStockAlertDTO
                    {
                        IngredientId = ingredient.IngredientId,
                        IngredientName = ingredient.IngredientName,
                        CurrentStock = ingredient.StockQuantity,
                        Threshold = ingredient.LowStockThreshold
                    });
                }
            }

            await repository.CreateAsync(issue, ct);

            return new CookingDeductionResultDTO
            {
                StockIssueId = issue.StockIssueId,
                Movements = issue.Movements.Select(MapMovement).ToList(),
                LowStockAlerts = lowStockAlerts
            };
        }, ct);
    }

    private static CookingMovementDTO MapMovement(StockMovement m)
    {
        return new CookingMovementDTO
        {
            StockMovementId = m.StockMovementId,
            StockIssueId = m.StockIssueId,
            IngredientId = m.IngredientId,
            GoodsReceiptItemId = m.GoodsReceiptItemId,
            MovementType = m.MovementType,
            QuantityChange = m.QuantityChange,
            CookingEventRef = m.CookingEventRef,
            OrderItemRef = m.OrderItemRef,
            PerformedByRef = m.PerformedByRef,
            Reason = m.Reason,
            CreatedAt = m.CreatedAt
        };
    }

    public async Task<StockIssue> GetById(Guid id, CancellationToken ct)
    {
        await access.RequireAsync("Read", ct);
        InventoryValidation.Id(id);
        return await repository.GetByIdAsync(id, ct) ?? throw new InventoryException(404, "Stock issue not found.");
    }
}
