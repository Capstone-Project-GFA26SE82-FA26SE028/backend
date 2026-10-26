using System.Globalization;
using Backend.Contracts.Catalog;
using Backend.Contracts.Inventory;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ordering.API.Clients;
using Ordering.Common.Exceptions;
using Ordering.Common.Settings;
using Ordering.DAL.Models;
using Xunit;

namespace Ordering.Tests;

public sealed class CookingInventoryClientTests
{
    [Fact]
    public async Task DeductCookingIngredientsAsync_ResolvesRecipeAndCallsInventoryDeduct()
    {
        var invoker = new TestCallInvoker();
        var beefId = Guid.NewGuid();
        var recipeId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        // Setup recipe returned by Catalog
        invoker.RecipeReply = new RecipeReply
        {
            IsSuccess = true,
            StatusCode = 200,
            Result = new RecipeInfo
            {
                RecipeVersionId = recipeId.ToString(),
                ItemId = itemId.ToString(),
                Version = 1,
                Ingredients =
                {
                    new RecipeIngredientInfo
                    {
                        IngredientRef = beefId.ToString(),
                        Quantity = "200",
                        BaseQuantitySnapshot = "200"
                    }
                }
            }
        };

        invoker.DeductionReply = new CookingDeductionReply
        {
            IsSuccess = true,
            StatusCode = 200,
            StockIssueId = Guid.NewGuid().ToString()
        };

        var client = CreateClient(invoker);

        var cookingEventId = Guid.NewGuid();
        var task = new KitchenTask
        {
            KitchenTaskId = Guid.NewGuid(),
            OrderItemId = Guid.NewGuid(),
            Quantity = 3,
            OrderItem = new OrderItem
            {
                OrderItemId = Guid.NewGuid(),
                ItemRef = itemId,
                RecipeVersionRef = recipeId,
                NameSnapshot = "Bò né",
                Quantity = 3
            }
        };

        await client.DeductCookingIngredientsAsync(cookingEventId, [task], default);

        Assert.NotNull(invoker.LastDeductionRequest);
        Assert.Equal(cookingEventId.ToString(), invoker.LastDeductionRequest.CookingEventId);
        Assert.Single(invoker.LastDeductionRequest.Items);

        var deductionItem = invoker.LastDeductionRequest.Items[0];
        Assert.Equal(task.KitchenTaskId.ToString(), deductionItem.TaskId);
        Assert.Equal("Bò né", deductionItem.DishName);
        Assert.Single(deductionItem.Ingredients);
        Assert.Equal(beefId.ToString(), deductionItem.Ingredients[0].IngredientId);
        // Base qty 200 * portion qty 3 = 600
        Assert.Equal("600", deductionItem.Ingredients[0].Quantity);
    }

    [Fact]
    public async Task DeductCookingIngredientsAsync_InventoryReturnsAborted_Throws409()
    {
        var invoker = new TestCallInvoker();
        var recipeId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        invoker.RecipeReply = new RecipeReply
        {
            IsSuccess = true,
            StatusCode = 200,
            Result = new RecipeInfo
            {
                RecipeVersionId = recipeId.ToString(),
                ItemId = itemId.ToString(),
                Ingredients =
                {
                    new RecipeIngredientInfo
                    {
                        IngredientRef = Guid.NewGuid().ToString(),
                        BaseQuantitySnapshot = "100"
                    }
                }
            }
        };

        invoker.DeductionException = new RpcException(new Status(StatusCode.Aborted, "Hết hàng tồn kho"));

        var client = CreateClient(invoker);

        var task = new KitchenTask
        {
            KitchenTaskId = Guid.NewGuid(),
            OrderItemId = Guid.NewGuid(),
            Quantity = 1,
            OrderItem = new OrderItem
            {
                OrderItemId = Guid.NewGuid(),
                ItemRef = itemId,
                RecipeVersionRef = recipeId,
                NameSnapshot = "Món A",
                Quantity = 1
            }
        };

        var ex = await Assert.ThrowsAsync<OrderingException>(() =>
            client.DeductCookingIngredientsAsync(Guid.NewGuid(), [task], default));

        Assert.Equal(409, ex.StatusCode);
        Assert.Contains("Hết hàng tồn kho", ex.Message);
    }

    [Fact]
    public async Task DeductCookingIngredientsAsync_DishWithoutRecipe_DoesNotCallInventory()
    {
        var invoker = new TestCallInvoker();
        invoker.RecipeException = new RpcException(new Status(StatusCode.NotFound, "No recipe"));

        var client = CreateClient(invoker);

        var task = new KitchenTask
        {
            KitchenTaskId = Guid.NewGuid(),
            OrderItemId = Guid.NewGuid(),
            Quantity = 1,
            OrderItem = new OrderItem
            {
                OrderItemId = Guid.NewGuid(),
                ItemRef = Guid.NewGuid(),
                NameSnapshot = "Coca Cola",
                Quantity = 1
            }
        };

        await client.DeductCookingIngredientsAsync(Guid.NewGuid(), [task], default);

        Assert.Null(invoker.LastDeductionRequest);
    }

    private static CookingInventoryClient CreateClient(TestCallInvoker invoker)
    {
        var catalogClient = new RecipeGrpc.RecipeGrpcClient(invoker);
        var inventoryClient = new StockIssueGrpc.StockIssueGrpcClient(invoker);
        var http = new HttpContextAccessor();
        var settings = Options.Create(new OrderingSettings { DependencyTimeoutSeconds = 5 });
        var logger = NullLogger<CookingInventoryClient>.Instance;

        return new CookingInventoryClient(catalogClient, inventoryClient, http, settings, logger);
    }

    private sealed class TestCallInvoker : CallInvoker
    {
        public RecipeReply? RecipeReply { get; set; }
        public RpcException? RecipeException { get; set; }

        public CookingDeductionReply? DeductionReply { get; set; }
        public RpcException? DeductionException { get; set; }
        public CookingDeductionRequest? LastDeductionRequest { get; set; }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request)
        {
            if (method.Name == "GetRecipeById" || method.Name == "GetActiveItemRecipe" || method.Name == "GetActiveOptionRecipe")
            {
                if (RecipeException is not null)
                {
                    throw RecipeException;
                }

                var response = (TResponse)(object)(RecipeReply ?? new RecipeReply());
                return CreateAsyncUnaryCall(response);
            }

            if (method.Name == "DeductForCooking")
            {
                if (request is CookingDeductionRequest req)
                {
                    LastDeductionRequest = req;
                }

                if (DeductionException is not null)
                {
                    throw DeductionException;
                }

                var response = (TResponse)(object)(DeductionReply ?? new CookingDeductionReply { IsSuccess = true, StatusCode = 200 });
                return CreateAsyncUnaryCall(response);
            }

            throw new NotImplementedException($"Method {method.Name} not mocked.");
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request)
        {
            throw new NotImplementedException();
        }

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options)
        {
            throw new NotImplementedException();
        }

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options,
            TRequest request)
        {
            throw new NotImplementedException();
        }

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            string? host,
            CallOptions options)
        {
            throw new NotImplementedException();
        }

        private static AsyncUnaryCall<T> CreateAsyncUnaryCall<T>(T response)
        {
            return new AsyncUnaryCall<T>(
                Task.FromResult(response),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => { });
        }
    }
}
