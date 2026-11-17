using Backend.Contracts.Billing;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Grpc.Core;

namespace Billing.API.GrpcServices;

public sealed class PaymentProviderConfigGrpcService(IPaymentProviderConfigService service)
    : PaymentProviderConfigGrpc.PaymentProviderConfigGrpcBase
{
    public override async Task<PayOsConfigurationInfo> GetById(PayOsConfigurationRequest request, ServerCallContext context)
    {
        return Map(await service.GetById(Guid.Parse(request.RestaurantId), context.CancellationToken));
    }

    public override async Task<PayOsConfigurationInfo> Update(SavePayOsConfigurationRequest request, ServerCallContext context)
    {
        return Map(await service.Update(Guid.Parse(request.RestaurantId), new SavePayOsConfigurationDTO
        {
            ClientId = request.ClientId,
            ApiKey = request.ApiKey,
            ChecksumKey = request.ChecksumKey,
            ReturnUrl = request.ReturnUrl,
            CancelUrl = request.CancelUrl,
            Enabled = request.Enabled
        }, context.CancellationToken));
    }

    private static PayOsConfigurationInfo Map(PayOsConfigurationDTO value)
    {
        return new PayOsConfigurationInfo
        {
            RestaurantId = value.RestaurantId.ToString(),
            Configured = value.Configured,
            ClientId = value.ClientId,
            ReturnUrl = value.ReturnUrl,
            CancelUrl = value.CancelUrl,
            WebhookUrl = value.WebhookUrl,
            Enabled = value.Enabled
        };
    }
}
