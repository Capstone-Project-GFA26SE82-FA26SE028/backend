using Billing.Common.Constants;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;

namespace Billing.BLL.Implements;

internal static class ReportFilterRules
{
    public static string Channel(Guid[]? orders, IReadOnlyDictionary<Guid, string> channels)
    {
        if (orders is null || orders.Length == 0 || orders.Any(id => !channels.TryGetValue(id, out var value)
            || value is not ("DineIn" or "Takeaway")))
        {
            return "Unclassified";
        }

        var values = orders.Select(id => channels[id]).Distinct().ToArray();
        return values.Length == 1 ? values[0] : "Mixed";
    }

    public static SalesReportFilterDTO Normalize(SalesReportFilterDTO? filters, string cashMethod)
    {
        filters ??= new SalesReportFilterDTO();
        var channel = filters.Channel?.Trim().ToLowerInvariant() switch
        {
            "all" => "all",
            "dinein" => "DineIn",
            "takeaway" => "Takeaway",
            "mixed" => "Mixed",
            "unclassified" => "Unclassified",
            _ => throw new BillingException(400, "Kênh phải là all, DineIn, Takeaway, Mixed hoặc Unclassified.")
        };
        var method = filters.MethodCode?.Trim();
        if (string.Equals(method, "all", StringComparison.OrdinalIgnoreCase))
        {
            method = "all";
        }
        else if (string.Equals(method, cashMethod, StringComparison.OrdinalIgnoreCase))
        {
            method = cashMethod;
        }
        else if (string.Equals(method, PayOsStates.Method, StringComparison.OrdinalIgnoreCase))
        {
            method = PayOsStates.Method;
        }
        else
        {
            throw new BillingException(400, "Phương thức báo cáo phải là all hoặc phương thức Cash/BankTransfer đang được hỗ trợ.");
        }

        var shift = filters.Shift?.Trim().ToLowerInvariant();
        if (shift is not ("all" or "unassigned"))
        {
            if (!Guid.TryParse(shift, out var id) || id == Guid.Empty)
            {
                throw new BillingException(400, "Ca phải là all, unassigned hoặc mã ca hợp lệ.");
            }

            shift = id.ToString();
        }

        return new SalesReportFilterDTO
        {
            Channel = channel,
            MethodCode = method,
            Shift = shift
        };
    }

}
