extern alias Gateway;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Billing.API.GrpcServices;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shared.Common.Pagination;
using Wire = Gateway::Backend.Contracts.Billing;

namespace Billing.Tests;

public sealed class PromotionNullMappingTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task GrpcPreservesOptionalTargetPresence(bool hasItem, bool hasCategory)
    {
        var promotion = Promotion(hasItem, hasCategory);
        await using var api = await StartBilling(new PromotionService(promotion));
        using var channel = Channel(api);
        var client = new Wire.PromotionGrpc.PromotionGrpcClient(channel);

        var reply = await client.GetAllAsync(new Wire.PromotionListRequest
        {
            Page = 1,
            PageSize = 10
        });

        var target = Assert.Single(Assert.Single(reply.Items).Targets);
        Assert.Equal(hasItem, target.HasItemRef);
        Assert.Equal(hasCategory, target.HasCategoryRef);
        if (hasItem)
        {
            Assert.Equal(promotion.Targets[0].ItemRef.ToString(), target.ItemRef);
        }

        if (hasCategory)
        {
            Assert.Equal(promotion.Targets[0].CategoryRef.ToString(), target.CategoryRef);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task GatewayPreservesNullableTargetsAcrossPromotionEndpoints(bool hasItem, bool hasCategory)
    {
        var promotion = Promotion(hasItem, hasCategory);
        var service = new PromotionService(promotion);
        await using var api = await StartBilling(service);
        using var channel = Channel(api);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(Gateway::ApiGateway.ApiAssembly).Assembly);
        builder.Services.Configure<Gateway::ApiGateway.Settings.BillingClientSettings>(options =>
        {
            options.TimeoutSeconds = 15;
        });
        builder.Services.AddSingleton(new Wire.PromotionGrpc.PromotionGrpcClient(channel));
        await using var gateway = builder.Build();
        gateway.MapControllers();
        await gateway.StartAsync();
        using var client = gateway.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "mapping-test");

        using var created = await client.PostAsJsonAsync("/api/promotions", new
        {
            promotion.PromotionName,
            promotion.DiscountType,
            promotion.Type,
            promotion.DiscountValue,
            promotion.StartAt,
            promotion.IsActive,
            Targets = new[]
            {
                new
                {
                    promotion.Targets[0].ItemRef,
                    promotion.Targets[0].CategoryRef,
                    Role = "AppliesTo",
                    RequiredQuantity = 1
                }
            }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        AssertTarget(await created.Content.ReadFromJsonAsync<JsonElement>(), promotion, false);
        var received = Assert.Single(Assert.IsType<CreatePromotionDTO>(service.ReceivedCreate).Targets);
        Assert.Equal(promotion.Targets[0].ItemRef, received.ItemRef);
        Assert.Equal(promotion.Targets[0].CategoryRef, received.CategoryRef);

        using var listed = await client.GetAsync("/api/promotions");
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        AssertTarget(await listed.Content.ReadFromJsonAsync<JsonElement>(), promotion, true);
        using var detail = await client.GetAsync($"/api/promotions/{promotion.PromotionId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        AssertTarget(await detail.Content.ReadFromJsonAsync<JsonElement>(), promotion, false);
        using var updated = await client.PutAsJsonAsync($"/api/promotions/{promotion.PromotionId}", new
        {
            PromotionName = "Kiểm tra ánh xạ"
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        AssertTarget(await updated.Content.ReadFromJsonAsync<JsonElement>(), promotion, false);
        using var status = await client.PatchAsJsonAsync($"/api/promotions/{promotion.PromotionId}/status", new
        {
            IsActive = true
        });
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        AssertTarget(await status.Content.ReadFromJsonAsync<JsonElement>(), promotion, false);
    }

    private static void AssertTarget(JsonElement body, PromotionDTO promotion, bool page)
    {
        var result = body.GetProperty("result");
        var item = page ? Assert.Single(result.GetProperty("items").EnumerateArray()) : result;
        var target = Assert.Single(item.GetProperty("targets").EnumerateArray());
        foreach (var (field, expected) in new[]
        {
            ("itemRef", promotion.Targets[0].ItemRef),
            ("categoryRef", promotion.Targets[0].CategoryRef)
        })
        {
            if (expected.HasValue)
            {
                Assert.Equal(expected.Value, target.GetProperty(field).GetGuid());
            }
            else
            {
                Assert.Equal(JsonValueKind.Null, target.GetProperty(field).ValueKind);
            }
        }
    }

    private static PromotionDTO Promotion(bool hasItem, bool hasCategory)
    {
        var id = Guid.NewGuid();
        return new PromotionDTO
        {
            PromotionId = id,
            PromotionName = "Khuyến mãi kiểm thử",
            DiscountType = "Percentage",
            Type = "Order",
            DiscountValue = 10,
            StartAt = DateTime.UtcNow,
            IsActive = true,
            Targets =
            [
                new PromotionTargetDTO
                {
                    PromotionTargetId = Guid.NewGuid(),
                    PromotionId = id,
                    ItemRef = hasItem ? Guid.NewGuid() : null,
                    CategoryRef = hasCategory ? Guid.NewGuid() : null,
                    Role = "AppliesTo",
                    RequiredQuantity = 1
                }
            ]
        };
    }

    private static GrpcChannel Channel(WebApplication api)
    {
        return GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = api.GetTestServer().CreateHandler()
        });
    }

    private static async Task<WebApplication> StartBilling(IPromotionService service)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddGrpc(options => options.Interceptors.Add<BillingInterceptor>());
        builder.Services.AddSingleton(service);
        var api = builder.Build();
        api.MapGrpcService<PromotionGrpcService>();
        await api.StartAsync();
        return api;
    }

    private sealed class PromotionService(PromotionDTO promotion) : IPromotionService
    {
        public CreatePromotionDTO? ReceivedCreate { get; private set; }

        public Task<PromotionDTO> Create(CreatePromotionDTO dto, CancellationToken ct)
        {
            ReceivedCreate = dto;
            return Task.FromResult(promotion);
        }

        public Task<PromotionDTO> Update(Guid id, UpdatePromotionDTO dto, CancellationToken ct)
        {
            return Task.FromResult(promotion);
        }

        public Task<PagedResult<PromotionDTO>> GetAll(PromotionQueryDTO query, CancellationToken ct)
        {
            return Task.FromResult(new PagedResult<PromotionDTO>([promotion], 1, query.Page, query.PageSize));
        }

        public Task<PromotionDTO> GetById(Guid id, CancellationToken ct)
        {
            return Task.FromResult(promotion);
        }

        public Task<PromotionDTO> SetStatus(Guid id, bool isActive, CancellationToken ct)
        {
            return Task.FromResult(promotion);
        }

        public Task<List<ApplicablePromotionDTO>> GetAvailableForBill(Guid billId, CancellationToken ct)
        {
            return Task.FromResult(new List<ApplicablePromotionDTO>());
        }
    }
}
