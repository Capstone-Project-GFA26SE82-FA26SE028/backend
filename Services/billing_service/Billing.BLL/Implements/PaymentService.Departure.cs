using Billing.Common.DTOs;
using Billing.Common.Exceptions;

namespace Billing.BLL.Implements;

public sealed partial class PaymentService
{
    public async Task RequirePaid(Guid sessionId, Guid restaurantId, CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(restaurantId, ct);
        RequireDepartureWaiter(actor, restaurantId);
        await settlement.GetPaid(sessionId, restaurantId, ct);
        var verified = await access.GetCurrentScopeAsync(restaurantId, ct);
        RequireDepartureWaiter(verified, restaurantId);
        if (actor.UserId != verified.UserId)
        {
            throw new BillingException(401, "Phiên đăng nhập nhân viên đã thay đổi.");
        }
    }

    private void RequireDepartureWaiter(BillingAccessScope actor, Guid restaurantId)
    {
        if (actor.UserId == Guid.Empty || actor.IsAdministrator
            || !roles.Value.WaiterRoleIds.Contains(actor.RoleId, StringComparer.Ordinal)
            || !actor.RestaurantIds.Contains(restaurantId))
        {
            throw new BillingException(403, "Chỉ phục vụ được phân công tại nhà hàng mới được xem xác nhận thanh toán theo phiên.");
        }
    }
}
