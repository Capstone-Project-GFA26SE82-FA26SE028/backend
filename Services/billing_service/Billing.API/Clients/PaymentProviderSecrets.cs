using System.Security.Cryptography;
using System.Text.Json;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Microsoft.AspNetCore.DataProtection;

namespace Billing.API.Clients;

public sealed class PaymentProviderSecrets(IDataProtectionProvider protection) : IPaymentProviderSecrets
{
    public string Protect(Guid restaurantId, PayOsCredentials credentials)
    {
        return Protector(restaurantId).Protect(JsonSerializer.Serialize(credentials));
    }

    public PayOsCredentials Unprotect(Guid restaurantId, string ciphertext)
    {
        try
        {
            return JsonSerializer.Deserialize<PayOsCredentials>(Protector(restaurantId).Unprotect(ciphertext))
                ?? throw new CryptographicException();
        }
        catch (Exception error) when (error is CryptographicException or JsonException)
        {
            throw new BillingException(503, "Không thể đọc khóa PayOS của nhà hàng. Vui lòng kiểm tra khóa mã hóa trên máy chủ.");
        }
    }

    private IDataProtector Protector(Guid restaurantId)
    {
        return protection.CreateProtector("Restaurant.PayOS.v1", restaurantId.ToString("N"));
    }
}
