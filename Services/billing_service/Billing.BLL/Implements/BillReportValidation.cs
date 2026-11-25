using Billing.DAL.Models;

namespace Billing.BLL.Implements;

internal static class BillReportValidation
{
    public static bool RevenueMatches(Bill bill)
    {
        try
        {
            return bill.BillLines.Count > 0 && bill.SubTotal >= 0 && bill.DiscountAmount >= 0
                && bill.DiscountAmount <= bill.SubTotal && bill.VatAmount >= 0 && bill.ServiceAmount >= 0
                && bill.BillLines.All(value => value.Quantity >= 0 && value.UnitPriceSnapshot >= 0
                    && value.DiscountAllocated >= 0 && value.DiscountAllocated <= value.Quantity * value.UnitPriceSnapshot)
                && bill.BillLines.Sum(value => value.Quantity * value.UnitPriceSnapshot) == bill.SubTotal
                && bill.BillLines.Sum(value => value.DiscountAllocated) == bill.DiscountAmount
                && bill.BillLines.Sum(value => value.VatAllocated) == bill.VatAmount
                && bill.BillLines.Sum(value => value.ServiceAllocated) == bill.ServiceAmount
                && decimal.Round(bill.SubTotal - bill.DiscountAmount + bill.VatAmount + bill.ServiceAmount,
                    0, MidpointRounding.AwayFromZero) == bill.TotalAmount;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

}
