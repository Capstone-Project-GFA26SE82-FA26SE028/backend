using Backend.Contracts.Restaurant;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Ordering.BLL.Interfaces;
using Ordering.Common.Exceptions;
using Ordering.Common.Settings;

namespace Ordering.API.Clients;

public sealed class CheckoutAccess(IOrderingAccess guests, IBillingSessionClient sessions,
    RestaurantAccessGrpc.RestaurantAccessGrpcClient staff, IHttpContextAccessor http,
    IOptions<OrderingSettings> settings) : ICheckoutAccess
{
    public async Task RequireAsync(Guid sessionId, string guestToken, bool write, CancellationToken ct)
    {
        await Require(sessionId, guestToken, write, false, ct);
    }

    public async Task RequireRequesterAsync(Guid sessionId, string guestToken, CancellationToken ct)
    {
        await Require(sessionId, guestToken, true, true, ct);
    }

    private async Task Require(Guid sessionId, string guestToken, bool write, bool allowWaiter, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(guestToken))
        {
            await guests.RequireAsync(sessionId, null, guestToken, ct);
            return;
        }

        var branch = await sessions.GetRestaurantIdAsync(sessionId, ct);
        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            throw new OrderingException(401, "Yêu cầu phiên làm việc của nhân viên hoặc phiên quét QR hợp lệ.");
        }

        var actor = await staff.GetCurrentScopeAsync(new CurrentRestaurantScopeRequest
        {
            RestaurantId = branch.ToString()
        }, new Metadata
        {
            { "authorization", authorization }
        }, deadline: DateTime.UtcNow.AddSeconds(settings.Value.DependencyTimeoutSeconds), cancellationToken: ct);
        if (!Guid.TryParse(actor.UserId, out var userId) || userId == Guid.Empty || string.IsNullOrWhiteSpace(actor.RoleId))
        {
            throw new OrderingException(503, "Dịch vụ nhà hàng trả về phạm vi nhân viên không hợp lệ.");
        }

        var allowed = settings.Value.CheckoutCashierRoleIds.Contains(actor.RoleId, StringComparer.Ordinal)
            || (allowWaiter && settings.Value.CheckoutWaiterRoleIds.Contains(actor.RoleId, StringComparer.Ordinal));
        if (write && (actor.IsAdministrator || !allowed
            || !actor.RestaurantIds.Contains(branch.ToString())))
        {
            throw new OrderingException(403, "Chỉ thu ngân được phân công mới có thể quản lý thanh toán.");
        }
    }
}
