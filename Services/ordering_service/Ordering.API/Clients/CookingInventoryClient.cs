using System.Globalization;
using Backend.Contracts.Catalog;
using Backend.Contracts.Inventory;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Ordering.BLL.Interfaces;
using Ordering.Common.Exceptions;
using Ordering.Common.Settings;
using Ordering.DAL.Models;

namespace Ordering.API.Clients;

public sealed class CookingInventoryClient(
    RecipeGrpc.RecipeGrpcClient catalogRecipeClient,
    StockIssueGrpc.StockIssueGrpcClient inventoryStockClient,
    IHttpContextAccessor http,
    IOptions<OrderingSettings> settings,
    ILogger<CookingInventoryClient> logger) : ICookingInventoryClient
{
    public async Task DeductCookingIngredientsAsync(
        Guid cookingEventId,
        IReadOnlyList<KitchenTask> tasks,
        CancellationToken ct)
    {
        if (cookingEventId == Guid.Empty)
        {
            throw new OrderingException(400, "Invalid cooking event ID.");
        }

        if (tasks is null || tasks.Count == 0)
        {
            return;
        }

        var headers = new Metadata();
        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(authorization))
        {
            headers.Add("authorization", authorization);
        }

        var deadline = DateTime.UtcNow.AddSeconds(settings.Value.DependencyTimeoutSeconds);
        var deductionRequest = new CookingDeductionRequest
        {
            CookingEventId = cookingEventId.ToString()
        };

        foreach (var task in tasks)
        {
            if (task.OrderItem is null)
            {
                continue;
            }

            var itemIngredients = new Dictionary<Guid, decimal>();

            RecipeInfo? dishRecipe = null;
            try
            {
                if (task.OrderItem.RecipeVersionRef.HasValue && task.OrderItem.RecipeVersionRef.Value != Guid.Empty)
                {
                    var recipeReply = await catalogRecipeClient.GetRecipeByIdAsync(
                        new RecipeIdRequest
                        {
                            RecipeVersionId = task.OrderItem.RecipeVersionRef.Value.ToString()
                        },
                        headers,
                        deadline: deadline,
                        cancellationToken: ct);

                    if (recipeReply.IsSuccess && recipeReply.Result is not null)
                    {
                        dishRecipe = recipeReply.Result;
                    }
                }
                else if (task.OrderItem.ItemRef != Guid.Empty)
                {
                    var recipeReply = await catalogRecipeClient.GetActiveItemRecipeAsync(
                        new ActiveItemRecipeRequest
                        {
                            ItemId = task.OrderItem.ItemRef.ToString()
                        },
                        headers,
                        deadline: deadline,
                        cancellationToken: ct);

                    if (recipeReply.IsSuccess && recipeReply.Result is not null)
                    {
                        dishRecipe = recipeReply.Result;
                    }
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
            {
            }

            if (dishRecipe is not null)
            {
                foreach (var ing in dishRecipe.Ingredients)
                {
                    var qtyStr = string.IsNullOrEmpty(ing.BaseQuantitySnapshot) || ing.BaseQuantitySnapshot == "0"
                        ? ing.Quantity
                        : ing.BaseQuantitySnapshot;

                    if (Guid.TryParse(ing.IngredientRef, out var ingredientId)
                        && decimal.TryParse(qtyStr, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var baseQty)
                        && baseQty > 0)
                    {
                        var totalLineQty = baseQty * task.Quantity;
                        if (itemIngredients.TryGetValue(ingredientId, out var existing))
                        {
                            itemIngredients[ingredientId] = existing + totalLineQty;
                        }
                        else
                        {
                            itemIngredients[ingredientId] = totalLineQty;
                        }
                    }
                }
            }

            if (task.OrderItem.OrderItemOptions is not null && task.OrderItem.OrderItemOptions.Count > 0)
            {
                foreach (var option in task.OrderItem.OrderItemOptions)
                {
                    if (option.OptionRef == Guid.Empty)
                    {
                        continue;
                    }

                    RecipeInfo? optionRecipe = null;
                    try
                    {
                        var optionReply = await catalogRecipeClient.GetActiveOptionRecipeAsync(
                            new ActiveOptionRecipeRequest
                            {
                                OptionId = option.OptionRef.ToString()
                            },
                            headers,
                            deadline: deadline,
                            cancellationToken: ct);

                        if (optionReply.IsSuccess && optionReply.Result is not null)
                        {
                            optionRecipe = optionReply.Result;
                        }
                    }
                    catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
                    {
                    }

                    if (optionRecipe is not null)
                    {
                        foreach (var ing in optionRecipe.Ingredients)
                        {
                            var qtyStr = string.IsNullOrEmpty(ing.BaseQuantitySnapshot) || ing.BaseQuantitySnapshot == "0"
                                ? ing.Quantity
                                : ing.BaseQuantitySnapshot;

                            if (Guid.TryParse(ing.IngredientRef, out var ingredientId)
                                && decimal.TryParse(qtyStr, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var baseQty)
                                && baseQty > 0)
                            {
                                var totalLineQty = baseQty * task.Quantity * option.Quantity;
                                if (itemIngredients.TryGetValue(ingredientId, out var existing))
                                {
                                    itemIngredients[ingredientId] = existing + totalLineQty;
                                }
                                else
                                {
                                    itemIngredients[ingredientId] = totalLineQty;
                                }
                            }
                        }
                    }
                }
            }

            if (itemIngredients.Count > 0)
            {
                var itemDeduction = new CookingItemDeduction
                {
                    TaskId = task.KitchenTaskId.ToString(),
                    OrderItemId = task.OrderItemId.ToString(),
                    DishName = task.OrderItem.NameSnapshot ?? string.Empty,
                    Quantity = task.Quantity
                };

                foreach (var (ingId, qty) in itemIngredients)
                {
                    itemDeduction.Ingredients.Add(new StockIssueLine
                    {
                        IngredientId = ingId.ToString(),
                        Quantity = qty.ToString("0.######", CultureInfo.InvariantCulture)
                    });
                }

                deductionRequest.Items.Add(itemDeduction);
            }
        }

        if (deductionRequest.Items.Count == 0)
        {
            return;
        }

        try
        {
            var reply = await inventoryStockClient.DeductForCookingAsync(
                deductionRequest,
                headers,
                deadline: deadline,
                cancellationToken: ct);

            if (reply.LowStockAlerts is not null && reply.LowStockAlerts.Count > 0)
            {
                foreach (var alert in reply.LowStockAlerts)
                {
                    logger.LogWarning(
                        "Nguyên liệu {IngredientName} ({IngredientId}) đã chạm hoặc xuống dưới ngưỡng tồn kho tối thiểu. Tồn hiện tại: {CurrentStock}, Ngưỡng: {Threshold}",
                        alert.IngredientName,
                        alert.IngredientId,
                        alert.CurrentStock,
                        alert.Threshold);
                }
            }
        }
        catch (RpcException ex) when (!ct.IsCancellationRequested)
        {
            var status = ex.StatusCode switch
            {
                StatusCode.Aborted or StatusCode.FailedPrecondition => 409,
                StatusCode.InvalidArgument => 400,
                StatusCode.NotFound => 404,
                StatusCode.PermissionDenied => 403,
                StatusCode.Unauthenticated => 401,
                StatusCode.DeadlineExceeded => 504,
                _ => 503
            };

            throw new OrderingException(status, ex.Status.Detail);
        }
    }
}
