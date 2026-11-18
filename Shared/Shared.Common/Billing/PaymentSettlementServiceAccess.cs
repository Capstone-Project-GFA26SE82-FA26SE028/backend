using System.Security.Cryptography;
using System.Text;

namespace Shared.Common.Billing;

public static class PaymentSettlementServiceAccess
{
    public const string ConfigurationKey = "Notifications:ServiceKey";
    public const string Header = "x-payment-settlement-key";

    public static bool IsConfigured(string? key)
    {
        return !string.IsNullOrWhiteSpace(key) && key.Length >= 32;
    }

    public static bool Matches(string? configured, string? supplied)
    {
        return IsConfigured(configured) && supplied is { Length: 64 }
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(CreateHeaderValue(configured!)),
                Encoding.UTF8.GetBytes(supplied));
    }

    public static string CreateHeaderValue(string key)
    {
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), "billing:paid-session-closure:v1"u8));
    }
}
