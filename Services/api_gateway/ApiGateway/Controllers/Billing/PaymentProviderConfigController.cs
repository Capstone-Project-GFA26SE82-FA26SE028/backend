using ApiGateway.DTOs.Billing;
using ApiGateway.Settings;
using ApiGateway.Swagger;
using Backend.Contracts.Billing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ApiGateway.Controllers.Billing;

[ApiController]
[Route("api/restaurants/{restaurantId:guid}/payment-settings/payos")]
[RequireBearerToken]
public sealed class PaymentProviderConfigController(PaymentProviderConfigGrpc.PaymentProviderConfigGrpcClient client,
    IOptions<BillingClientSettings> settings) : BillingControllerBase(settings)
{
    [HttpGet]
    public Task<IActionResult> GetById(Guid restaurantId, CancellationToken ct)
    {
        return Execute(async options => Map(await client.GetByIdAsync(new PayOsConfigurationRequest
        {
            RestaurantId = restaurantId.ToString()
        }, options)), ct);
    }

    [HttpPut]
    [RequestSizeLimit(16384)]
    public Task<IActionResult> Update(Guid restaurantId, SavePayOsConfigurationDTO body, CancellationToken ct)
    {
        return Execute(async options => Map(await client.UpdateAsync(new SavePayOsConfigurationRequest
        {
            RestaurantId = restaurantId.ToString(),
            ClientId = body.ClientId ?? string.Empty,
            ApiKey = body.ApiKey ?? string.Empty,
            ChecksumKey = body.ChecksumKey ?? string.Empty,
            ReturnUrl = body.ReturnUrl ?? string.Empty,
            CancelUrl = body.CancelUrl ?? string.Empty,
            Enabled = body.Enabled
        }, options)), ct);
    }

    private static object Map(PayOsConfigurationInfo value)
    {
        return new
        {
            restaurantId = value.RestaurantId,
            configured = value.Configured,
            clientId = value.ClientId,
            returnUrl = value.ReturnUrl,
            cancelUrl = value.CancelUrl,
            webhookUrl = value.WebhookUrl,
            enabled = value.Enabled
        };
    }
}
