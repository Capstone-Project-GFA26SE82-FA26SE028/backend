using Billing.Common.DTOs;

namespace Billing.BLL.Interfaces;

public interface IPaymentProviderSecrets
{
    string Protect(Guid restaurantId, PayOsCredentials credentials);
    PayOsCredentials Unprotect(Guid restaurantId, string ciphertext);
}
