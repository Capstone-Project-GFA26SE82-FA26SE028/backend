using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Billing.API.Clients;

public static class PayOsSignature
{
    public static string SignPaymentRequest(long orderCode, long amount, string description,
        string cancelUrl, string returnUrl, string checksumKey)
    {
        var data = $"amount={amount.ToString(CultureInfo.InvariantCulture)}&cancelUrl={cancelUrl}&description={description}&orderCode={orderCode.ToString(CultureInfo.InvariantCulture)}&returnUrl={returnUrl}";
        return Sign(data, checksumKey);
    }

    public static string SignData(JsonElement data, string checksumKey)
    {
        if (data.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("PayOS signed data must be an object.");
        }

        return Sign(Query(data), checksumKey);
    }

    public static string PayloadHash(JsonElement data)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Query(data)))).ToLowerInvariant();
    }

    private static string Query(JsonElement data)
    {
        RequireUniqueProperties(data);
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        Flatten(data, string.Empty, values);
        return string.Join("&", values.Select(value => value.Key + "=" + value.Value));
    }

    private static void Flatten(JsonElement value, string prefix, IDictionary<string, string> values)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                Flatten(property.Value, prefix.Length == 0 ? property.Name : prefix + "." + property.Name, values);
            }
        }
        else if (!values.TryAdd(prefix, Scalar(value)))
        {
            throw new JsonException("Ambiguous signed property path.");
        }
    }

    public static bool VerifyData(JsonElement data, string? signature, string checksumKey)
    {
        if (string.IsNullOrWhiteSpace(checksumKey) || signature is null || signature.Length != 64)
        {
            return false;
        }

        try
        {
            var expected = Convert.FromHexString(SignData(data, checksumKey));
            var supplied = Convert.FromHexString(signature);
            return CryptographicOperations.FixedTimeEquals(expected, supplied);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Sign(string text, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("A PayOS checksum key is required.", nameof(key));
        }

        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static string Scalar(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null => string.Empty,
            JsonValueKind.String => value.GetString() is "null" or "undefined" ? string.Empty : value.GetString()!,
            JsonValueKind.Number => value.TryGetDecimal(out var number)
                ? number.ToString("G29", CultureInfo.InvariantCulture)
                : throw new JsonException("Unsupported numeric value in PayOS signed data."),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Array => CanonicalArray(value),
            _ => throw new JsonException("Unsupported value in PayOS signed data.")
        };
    }

    private static string CanonicalArray(JsonElement value)
    {
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }))
        {
            WriteCanonical(writer, value);
        }

        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }

            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray())
            {
                WriteCanonical(writer, item);
            }

            writer.WriteEndArray();
        }
        else
        {
            value.WriteTo(writer);
        }
    }

    private static void RequireUniqueProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new JsonException("Duplicate property in PayOS signed data.");
                }

                RequireUniqueProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                RequireUniqueProperties(item);
            }
        }
    }
}
