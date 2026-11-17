using Billing.BLL.Interfaces;
using Billing.Common.Settings;
using Billing.DAL.Interfaces;
using Microsoft.Extensions.Options;
using Shared.Persistence.RestaurantIsolation;

namespace Billing.API.Clients;

public sealed class RestaurantPayOsOptions(RestaurantDataScope scope, IPaymentProviderConfigRepository repository,
    IPaymentProviderSecrets secrets, IOptionsMonitor<PayOsSettings> defaults) : IOptions<PayOsSettings>
{
    private PayOsSettings? selected;

    public PayOsSettings Value
    {
        get
        {
            if (selected is not null)
            {
                return selected;
            }

            if (!scope.RestaurantId.HasValue)
            {
                return new PayOsSettings();
            }

            var configuration = repository.GetForRuntime(scope.RestaurantId.Value);
            if (configuration is null)
            {
                return new PayOsSettings();
            }

            var credentials = secrets.Unprotect(scope.RestaurantId.Value, configuration.SecretReference);
            var common = defaults.CurrentValue;
            selected = new PayOsSettings
            {
                Enabled = configuration.Enabled,
                ClientId = configuration.ClientId,
                ApiKey = credentials.ApiKey,
                ChecksumKey = credentials.ChecksumKey,
                ReturnUrl = configuration.ReturnUrl,
                CancelUrl = configuration.CancelUrl,
                WebhookUrl = configuration.WebhookUrl,
                BaseUrl = common.BaseUrl,
                LinkLifetimeMinutes = common.LinkLifetimeMinutes,
                RequestTimeoutSeconds = common.RequestTimeoutSeconds,
                ReconcileIntervalSeconds = common.ReconcileIntervalSeconds,
                ReconcileBatchSize = common.ReconcileBatchSize,
                PaidReconcileHours = common.PaidReconcileHours,
                AllowInsecureTestTransport = common.AllowInsecureTestTransport
            };
            return selected;
        }
    }
}
