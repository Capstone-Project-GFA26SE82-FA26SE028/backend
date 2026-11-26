using System.Data;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.Common.Settings;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.Extensions.Options;
using Shared.Persistence.Interfaces;
using Shared.Persistence.RestaurantIsolation;

namespace Billing.BLL.Implements;

public sealed class PaymentProviderConfigService(IPaymentProviderConfigRepository repository,
    IUnitOfWork work, IBillingAccess access, RestaurantDataScope scope, IPaymentProviderSecrets secrets,
    IOptions<PaymentProviderConfigurationSettings> settings) : IPaymentProviderConfigService
{
    public async Task<PayOsConfigurationDTO> GetById(Guid restaurantId, CancellationToken ct)
    {
        await Authorize(restaurantId, ct);
        return Map(restaurantId, await repository.GetByRestaurantAsync(restaurantId, ct));
    }

    public async Task<PayOsConfigurationDTO> Update(Guid restaurantId, SavePayOsConfigurationDTO request, CancellationToken ct)
    {
        await Authorize(restaurantId, ct);
        var returnUrl = string.IsNullOrWhiteSpace(request.ReturnUrl)
            ? "http://localhost:5173/payment/success"
            : request.ReturnUrl;
        var cancelUrl = string.IsNullOrWhiteSpace(request.CancelUrl)
            ? "http://localhost:5173/payment/cancel"
            : request.CancelUrl;

        if (!Token(request.ClientId, 255) || !Https(returnUrl) || !Https(cancelUrl))
        {
            throw new BillingException(400, "Vui lòng nhập Client ID và đường dẫn return, cancel hợp lệ.");
        }

        var webhook = Webhook(restaurantId);
        return await work.ExecuteInTransactionAsync(async () =>
        {
            var configuration = await repository.GetByRestaurantAsync(restaurantId, ct);

            if (configuration is null)
            {
                if (!Token(request.ApiKey, 512) || !Token(request.ChecksumKey, 512))
                {
                    throw new BillingException(400, "Vui lòng nhập đầy đủ API Key và Checksum Key khi cấu hình lần đầu.");
                }

                configuration = new PaymentProviderConfig
                {
                    PaymentProviderConfigId = Guid.NewGuid(),
                    RestaurantId = restaurantId,
                    Provider = "PayOS"
                };
                repository.Create(configuration);
            }

            configuration.ClientId = request.ClientId;

            if (!string.IsNullOrWhiteSpace(request.ApiKey) && !string.IsNullOrWhiteSpace(request.ChecksumKey))
            {
                if (!Token(request.ApiKey, 512) || !Token(request.ChecksumKey, 512))
                {
                    throw new BillingException(400, "API Key hoặc Checksum Key không hợp lệ.");
                }

                configuration.SecretReference = secrets.Protect(restaurantId, new PayOsCredentials
                {
                    ApiKey = request.ApiKey,
                    ChecksumKey = request.ChecksumKey
                });
            }
            configuration.ReturnUrl = returnUrl;
            configuration.CancelUrl = cancelUrl;
            configuration.WebhookUrl = webhook;
            configuration.Enabled = request.Enabled;
            configuration.IsVerified = false;
            return Map(restaurantId, configuration);
        }, IsolationLevel.Serializable, ct);
    }

    private async Task Authorize(Guid restaurantId, CancellationToken ct)
    {
        if (restaurantId == Guid.Empty)
        {
            throw new BillingException(400, "Vui lòng chọn nhà hàng.");
        }

        var actor = await access.GetCurrentScopeAsync(restaurantId, ct);
        if (actor.RoleId != "admin" || actor.IsAdministrator || !actor.RestaurantIds.Contains(restaurantId))
        {
            throw new BillingException(403, "Chỉ admin được phân công tại nhà hàng này mới được cấu hình PayOS.");
        }

        scope.Select(restaurantId);
    }

    private string Webhook(Guid restaurantId)
    {
        var rawBase = string.IsNullOrWhiteSpace(settings.Value.WebhookBaseUrl)
            ? "http://localhost:5000"
            : settings.Value.WebhookBaseUrl;
        var baseUrl = rawBase.TrimEnd('/');
        if (!Https(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            throw new BillingException(409, "Máy chủ chưa cấu hình PaymentProviderConfiguration:WebhookBaseUrl HTTPS.");
        }

        return baseUrl + $"/api/restaurants/{restaurantId}/payments/payos/webhook";
    }

    private PayOsConfigurationDTO Map(Guid restaurantId, PaymentProviderConfig? value)
    {
        return new PayOsConfigurationDTO
        {
            RestaurantId = restaurantId,
            Configured = value is not null,
            ClientId = value?.ClientId ?? string.Empty,
            Enabled = value?.Enabled ?? false,
            ReturnUrl = value?.ReturnUrl ?? string.Empty,
            CancelUrl = value?.CancelUrl ?? string.Empty,
            WebhookUrl = value?.WebhookUrl ?? Webhook(restaurantId)
        };
    }

    private static bool Token(string value, int max)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Length <= max && value.All(character => character is >= '!' and <= '~');
    }

    private static bool Https(string value)
    {
        return value.Length <= 1900 && Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            && uri.UserInfo.Length == 0;
    }
}
