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

    [HttpPost("test-qr")]
    [RequestSizeLimit(16384)]
    public Task<IActionResult> TestQr(Guid restaurantId,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] TestPayOsConfigurationDTO? body,
        CancellationToken ct)
    {
        return Execute(async options =>
        {
            var result = await client.TestQrAsync(new TestPayOsConfigurationRequest
            {
                RestaurantId = restaurantId.ToString(),
                ClientId = body?.ClientId ?? string.Empty,
                ApiKey = body?.ApiKey ?? string.Empty,
                ChecksumKey = body?.ChecksumKey ?? string.Empty,
                UseSavedConfiguration = body?.UseSavedConfiguration ?? false
            }, options);

            return new
            {
                accountName = result.AccountName,
                accountNumber = result.AccountNumber,
                bin = result.Bin,
                result.Amount,
                result.Description,
                result.OrderCode,
                result.QrCode,
                result.CheckoutUrl,
                result.PaymentLinkId,
                result.Status
            };
        }, ct);
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
