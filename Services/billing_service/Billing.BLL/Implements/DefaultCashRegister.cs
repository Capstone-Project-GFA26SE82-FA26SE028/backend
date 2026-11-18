using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;

namespace Billing.BLL.Implements;

internal static class DefaultCashRegister
{
    public const string Code = "DEFAULT";

    public static async Task<CashRegister?> FindAsync(ICashRegisterRepository registers, Guid restaurantId, CancellationToken ct)
    {
        var result = await registers.GetPageAsync(new CashRegisterQueryDTO
        {
            RestaurantId = restaurantId,
            Page = 1,
            PageSize = 2
        }, [restaurantId], ct);
        if (result.TotalCount > 1)
        {
            throw new BillingException(409, "Nhà hàng có nhiều quầy thu ngân cũ. Cần xác định một quầy mặc định trước khi tiếp tục.");
        }

        return result.Items.SingleOrDefault();
    }

    public static async Task<CashRegister> LockAsync(ICashRegisterRepository registers, Guid restaurantId, CancellationToken ct)
    {
        var entry = await FindAsync(registers, restaurantId, ct)
            ?? throw new BillingException(409, "Nhà hàng chưa có quầy mặc định. Thu ngân cần mở ca đầu tiên.");
        return await registers.LockByIdAsync(entry.CashRegisterId, ct)
            ?? throw new BillingException(409, "Quầy mặc định không còn khả dụng.");
    }
}
