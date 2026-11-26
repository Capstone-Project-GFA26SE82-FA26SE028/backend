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
    public async Task<IActionResult> TestQr(Guid restaurantId, [FromBody] TestPayOsConfigurationDTO body, CancellationToken ct)
    {
        var clientId = body.ClientId?.Trim();
        var apiKey = body.ApiKey?.Trim();
        var checksumKey = body.ChecksumKey?.Trim();

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(checksumKey))
        {
            return BadRequest(new { isSuccess = false, message = "Vui lòng cung cấp đầy đủ Client ID, API Key và Checksum Key để kiểm tra." });
        }

        try
        {
            var orderCode = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var description = "Kiem tra PayOS";
            var returnUrl = "http://localhost:5173/payment/success";
            var cancelUrl = "http://localhost:5173/payment/cancel";
            var signData = $"amount=2000&cancelUrl={cancelUrl}&description={description}&orderCode={orderCode}&returnUrl={returnUrl}";

            var signature = Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(checksumKey),
                System.Text.Encoding.UTF8.GetBytes(signData))).ToLowerInvariant();

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api-merchant.payos.vn/v2/payment-requests");
            request.Headers.Add("x-client-id", clientId);
            request.Headers.Add("x-api-key", apiKey);
            request.Content = JsonContent.Create(new
            {
                orderCode,
                amount = 2000,
                description,
                cancelUrl,
                returnUrl,
                signature
            });

            using var response = await httpClient.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            using var doc = System.Text.Json.JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            var code = root.TryGetProperty("code", out var codeProp) ? codeProp.GetString() : null;

            if (code == "00" && root.TryGetProperty("data", out var dataProp))
            {
                return Ok(new
                {
                    isSuccess = true,
                    result = new
                    {
                        accountName = dataProp.TryGetProperty("accountName", out var an) ? an.GetString() : null,
                        accountNumber = dataProp.TryGetProperty("accountNumber", out var acc) ? acc.GetString() : null,
                        bin = dataProp.TryGetProperty("bin", out var b) ? b.GetString() : null,
                        amount = 2000,
                        description,
                        orderCode,
                        qrCode = dataProp.TryGetProperty("qrCode", out var qr) ? qr.GetString() : null,
                        checkoutUrl = dataProp.TryGetProperty("checkoutUrl", out var co) ? co.GetString() : null
                    }
                });
            }

            var desc = root.TryGetProperty("desc", out var descProp) ? descProp.GetString() : "Thông tin xác thực PayOS không hợp lệ.";
            return BadRequest(new { isSuccess = false, message = "PayOS từ chối: " + desc });
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { isSuccess = false, message = "Không thể kết nối đến máy chủ PayOS: " + ex.Message });
        }
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
