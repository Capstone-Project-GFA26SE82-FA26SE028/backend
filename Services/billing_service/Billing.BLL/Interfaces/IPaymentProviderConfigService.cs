using Billing.Common.DTOs;

namespace Billing.BLL.Interfaces;

public interface IPaymentProviderConfigService
{
    Task<PayOsConfigurationDTO> GetById(Guid restaurantId, CancellationToken ct);
    Task<PayOsConfigurationDTO> Update(Guid restaurantId, SavePayOsConfigurationDTO request, CancellationToken ct);
}
