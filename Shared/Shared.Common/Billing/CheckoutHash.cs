using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Shared.Common.Billing;

public sealed record CheckoutHashOption(Guid OptionId, int Quantity, decimal ExtraPrice);

public sealed record CheckoutHashLine(Guid OrderItemId, Guid ItemId, decimal Quantity,
    decimal BasePrice, IReadOnlyList<CheckoutHashOption> Options);

public static class CheckoutHash
{
    public static string Compute(IEnumerable<CheckoutHashLine> lines)
    {
        var data = lines.Where(line => line.Quantity > 0).OrderBy(line => line.OrderItemId).Select(line => new
        {
            line.OrderItemId,
            line.ItemId,
            Quantity = Number(line.Quantity),
            BasePrice = Number(line.BasePrice),
            Options = line.Options.OrderBy(option => option.OptionId).Select(option => new
            {
                option.OptionId,
                option.Quantity,
                ExtraPrice = Number(option.ExtraPrice)
            })
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data))));
    }

    private static string Number(decimal value)
    {
        return value.ToString("0.############################", CultureInfo.InvariantCulture);
    }
}
