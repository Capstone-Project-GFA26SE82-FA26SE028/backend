using System.Globalization;
using ApiGateway.DTOs.Customer;
using ApiGateway.Swagger;
using Backend.Contracts.Customer;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Controllers.Customer;

[ApiController]
[Route("api/restaurants/{restaurantId:guid}/loyalty")]
[RequireBearerToken]
public sealed class ManagerLoyaltyController(CustomerGrpc.CustomerGrpcClient client) : CustomerControllerBase
{
    [HttpGet("tiers")]
    public Task<IActionResult> GetTiers(Guid restaurantId, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.GetRestaurantTiersAsync(new RestaurantTierListRequest
            {
                RestaurantId = restaurantId.ToString()
            }, ScopedOptions(restaurantId, ct));

            return reply.Items.Select(MapTier);
        }, ct);
    }

    [HttpPost("tiers")]
    public Task<IActionResult> CreateTier(Guid restaurantId, [FromBody] SaveLoyaltyTierRequestDTO body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.CreateTierAsync(new CreateLoyaltyTierRequest
            {
                RestaurantId = restaurantId.ToString(),
                LoyaltyTierCode = body.LoyaltyTierCode,
                LoyaltyTierName = body.LoyaltyTierName,
                RequiredSpend = body.RequiredSpend.ToString("0.##", CultureInfo.InvariantCulture),
                DiscountPercent = body.DiscountPercent.ToString("0.##", CultureInfo.InvariantCulture)
            }, ScopedOptions(restaurantId, ct));

            return MapTier(reply);
        }, ct, 201);
    }

    [HttpPut("tiers/{tierId:guid}")]
    public Task<IActionResult> UpdateTier(Guid restaurantId, Guid tierId, [FromBody] SaveLoyaltyTierRequestDTO body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.UpdateTierAsync(new UpdateLoyaltyTierRequest
            {
                RestaurantId = restaurantId.ToString(),
                LoyaltyTierId = tierId.ToString(),
                LoyaltyTierCode = body.LoyaltyTierCode,
                LoyaltyTierName = body.LoyaltyTierName,
                RequiredSpend = body.RequiredSpend.ToString("0.##", CultureInfo.InvariantCulture),
                DiscountPercent = body.DiscountPercent.ToString("0.##", CultureInfo.InvariantCulture)
            }, ScopedOptions(restaurantId, ct));

            return MapTier(reply);
        }, ct);
    }

    [HttpDelete("tiers/{tierId:guid}")]
    public Task<IActionResult> DeleteTier(Guid restaurantId, Guid tierId, CancellationToken ct)
    {
        return Execute(async () =>
        {
            await client.DeleteTierAsync(new DeleteLoyaltyTierRequest
            {
                RestaurantId = restaurantId.ToString(),
                LoyaltyTierId = tierId.ToString()
            }, ScopedOptions(restaurantId, ct));

            return new { success = true, message = "Đã xóa hạng thành viên thành công." };
        }, ct);
    }

    [HttpGet("policy")]
    public Task<IActionResult> GetPolicy(Guid restaurantId, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.GetRestaurantPolicyAsync(new RestaurantPolicyRequest
            {
                RestaurantId = restaurantId.ToString()
            }, ScopedOptions(restaurantId, ct));

            return MapPolicy(reply, restaurantId);
        }, ct);
    }

    [HttpPut("policy")]
    public Task<IActionResult> SavePolicy(Guid restaurantId, [FromBody] SaveLoyaltyPolicyRequestDTO body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.SavePolicyAsync(new SaveLoyaltyPolicyRequest
            {
                RestaurantId = restaurantId.ToString(),
                SpendPerPoint = body.SpendPerPoint.ToString("0.##", CultureInfo.InvariantCulture),
                MoneyPerRedeemedPoint = body.MoneyPerRedeemedPoint.ToString("0.##", CultureInfo.InvariantCulture)
            }, ScopedOptions(restaurantId, ct));

            return MapPolicy(reply, restaurantId);
        }, ct);
    }

    private CallOptions ScopedOptions(Guid restaurantId, CancellationToken ct)
    {
        var headers = new Metadata
        {
            { "x-restaurant-id", restaurantId.ToString() }
        };

        var auth = Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(auth))
        {
            headers.Add("authorization", auth);
        }

        return new CallOptions(headers, DateTime.UtcNow.AddSeconds(15), ct);
    }

    private static LoyaltyTierResponseDTO MapTier(LoyaltyTierInfo item) => new()
    {
        LoyaltyTierId = Guid.TryParse(item.LoyaltyTierId, out var id) ? id : Guid.Empty,
        LoyaltyTierCode = item.LoyaltyTierCode,
        LoyaltyTierName = item.LoyaltyTierName,
        RequiredSpend = decimal.TryParse(item.RequiredSpend, NumberStyles.Any, CultureInfo.InvariantCulture, out var spend) ? spend : 0m,
        DiscountPercent = decimal.TryParse(item.DiscountPercent, NumberStyles.Any, CultureInfo.InvariantCulture, out var discount) ? discount : 0m
    };

    private static LoyaltyPolicyResponseDTO MapPolicy(LoyaltyPolicyInfo item, Guid restaurantId) => new()
    {
        PolicyId = Guid.TryParse(item.PolicyId, out var id) ? id : Guid.Empty,
        RestaurantId = Guid.TryParse(item.RestaurantId, out var rid) ? rid : restaurantId,
        Version = item.Version,
        SpendPerPoint = decimal.TryParse(item.SpendPerPoint, NumberStyles.Any, CultureInfo.InvariantCulture, out var spend) ? spend : 0m,
        MoneyPerRedeemedPoint = decimal.TryParse(item.MoneyPerRedeemedPoint, NumberStyles.Any, CultureInfo.InvariantCulture, out var money) ? money : 0m,
        EffectiveFrom = DateTimeOffset.TryParse(item.EffectiveFrom, out var dto) ? dto : DateTimeOffset.UtcNow
    };
}
