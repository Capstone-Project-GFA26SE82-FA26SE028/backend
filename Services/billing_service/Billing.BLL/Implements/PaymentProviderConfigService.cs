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
        if (!Token(request.ClientId, 255) || !Token(request.ApiKey, 512) || !Token(request.ChecksumKey, 512)
            || !Https(request.ReturnUrl) || !Https(request.CancelUrl))
        {
            throw new BillingException(400, "Vui lòng nhập Client ID, API Key, Checksum Key và đường dẫn HTTPS return, cancel hợp lệ.");
        }

        var webhook = Webhook(restaurantId);
        return await work.ExecuteInTransactionAsync(async () =>
        {
            var configuration = await repository.GetByRestaurantAsync(restaurantId, ct);
            if (configuration is not null && configuration.ClientId != request.ClientId)
            {
                throw new BillingException(409, "Không thể đổi Client ID của kênh đã cấu hình vì các giao dịch cũ vẫn thuộc kênh đó. Có thể cập nhật khóa của cùng kênh.");
            }

            if (configuration is null)
            {
                configuration = new PaymentProviderConfig
                {
                    PaymentProviderConfigId = Guid.NewGuid(),
                    RestaurantId = restaurantId,
                    Provider = "PayOS"
                };
                repository.Create(configuration);
            }

            configuration.ClientId = request.ClientId;
            configuration.SecretReference = secrets.Protect(restaurantId, new PayOsCredentials
            {
                ApiKey = request.ApiKey,
                ChecksumKey = request.ChecksumKey
            });
            configuration.ReturnUrl = request.ReturnUrl;
            configuration.CancelUrl = request.CancelUrl;
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
        var baseUrl = settings.Value.WebhookBaseUrl.TrimEnd('/');
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
            && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0;
    }
}
