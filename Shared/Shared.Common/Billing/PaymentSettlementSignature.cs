using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Shared.Common.Billing;

public static class PaymentSettlementSignature
{
    public const string ReadPurpose = "billing.settlement.read.v1";
    public const string ProofPurpose = "billing.settlement.proof.v1";
    public const string ClosedPurpose = "restaurant.settlement.closed.v1";
    public const string TimestampHeader = "x-settlement-time";
    public const string SignatureHeader = "x-settlement-signature";
    private const int MaximumClockSkewSeconds = 60;

    public static bool IsConfigured(string key)
    {
        return TryKey(key, out _);
    }

    public static string Scope(Guid sessionId, Guid restaurantId)
    {
        return sessionId.ToString("N") + ":" + restaurantId.ToString("N");
    }

    public static string Proof(PaidSessionSettlement value)
    {
        return Scope(value.TableSessionId, value.RestaurantId) + ":" + value.BillId.ToString("N")
            + ":" + value.CheckoutId.ToString("N") + ":" + value.PaidAt.UtcTicks.ToString(CultureInfo.InvariantCulture);
    }

    public static string Closed(Guid sessionId, Guid restaurantId, DateTimeOffset closedAt)
    {
        return Scope(sessionId, restaurantId) + ":" + closedAt.UtcTicks.ToString(CultureInfo.InvariantCulture);
    }

    public static string Sign(string purpose, string payload, long timestamp, string key)
    {
        if (!TryKey(key, out var bytes))
        {
            throw new InvalidOperationException("Configure PaymentSettlement:SharedKey with at least 32 random Base64 bytes.");
        }

        var data = purpose + "\n" + timestamp.ToString(CultureInfo.InvariantCulture) + "\n" + payload;
        return Convert.ToHexString(HMACSHA256.HashData(bytes, Encoding.UTF8.GetBytes(data)));
    }

    public static bool Verify(string purpose, string payload, long timestamp, string signature, string key, DateTimeOffset now)
    {
        var current = now.ToUnixTimeSeconds();
        if (timestamp < current - MaximumClockSkewSeconds || timestamp > current + MaximumClockSkewSeconds
            || signature.Length != 64 || !TryKey(key, out _))
        {
            return false;
        }

        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(signature), Convert.FromHexString(Sign(purpose, payload, timestamp, key)));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool TryKey(string key, out byte[] bytes)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            bytes = [];
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(key);
            return bytes.Length >= 32;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }
}
