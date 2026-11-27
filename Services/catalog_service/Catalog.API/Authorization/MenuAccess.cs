using Backend.Contracts.Identity;
using Backend.Contracts.Restaurant;
using Catalog.BLL.Interfaces;
using Catalog.Common.Settings;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Shared.Persistence.RestaurantIsolation;

namespace Catalog.API.Authorization;

public sealed class MenuAccess(
    UserGrpc.UserGrpcClient identity,
    GuestAccessGrpc.GuestAccessGrpcClient guests,
    IHttpContextAccessor httpContextAccessor,
    IOptions<CatalogManagementSettings> settings,
    RestaurantDataScope scope) : IMenuAccess
{
    public Task RequireReadAsync(Guid? tableSessionId, string? guestToken, CancellationToken cancellationToken)
    {
        return ReadAsync(tableSessionId, guestToken, false, cancellationToken);
    }

    public Task RequireOnlineReadAsync(Guid restaurantId, CancellationToken cancellationToken)
    {
        if (restaurantId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Vui lòng cung cấp nhà hàng."));
        }

        return ReadAsync(null, null, true, cancellationToken);
    }

    public Task RequireTakeawayValidationAsync(Guid restaurantId, CancellationToken cancellationToken)
    {
        return ReadAsync(null, null, true, cancellationToken, restaurantId);
    }

    private async Task ReadAsync(Guid? tableSessionId, string? guestToken, bool online, CancellationToken cancellationToken,
        Guid? takeawayValidationRestaurantId = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(settings.Value.IdentityTimeoutSeconds);
        if (tableSessionId.HasValue)
        {
            if (tableSessionId == Guid.Empty || guestToken?.Length != 64 || !guestToken.All(Uri.IsHexDigit))
            {
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Yêu cầu phiên làm việc của khách hợp lệ."));
            }

            var guest = await guests.GetMeAsync(new GuestAccessRequest
            {
                TableSessionId = tableSessionId.Value.ToString(),
                Token = guestToken
            }, deadline: deadline, cancellationToken: cancellationToken);
            if (!Guid.TryParse(guest.TableSessionId, out var verifiedSession) || verifiedSession != tableSessionId)
            {
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Phiên làm việc của khách không hợp lệ."));
            }

            return;
        }

        var authorization = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Yêu cầu xác thực tài khoản."));
        }

        var reply = await identity.GetMeAsync(new GetMeRequest(), new Metadata
        {
            { "authorization", authorization }
        }, deadline: deadline, cancellationToken: cancellationToken);
        if (!reply.IsSuccess)
        {
            var status = reply.StatusCode switch
            {
                401 or 404 => StatusCode.Unauthenticated,
                403 => StatusCode.PermissionDenied,
                _ => StatusCode.Unavailable
            };
            throw new RpcException(new Status(status, "Không thể xác minh quyền truy cập thực đơn."));
        }

        if (reply.Result is null || !reply.Result.IsActive
            || !Guid.TryParse(reply.Result.UserId, out var userId) || userId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Tài khoản không hợp lệ hoặc đang bị khóa."));
        }

        if (online && reply.Result.RoleId == "customer")
        {
            return;
        }

        if (takeawayValidationRestaurantId.HasValue && reply.Result.RoleId == "cashier")
        {
            if (scope.RestaurantId != takeawayValidationRestaurantId)
            {
                throw new RpcException(new Status(StatusCode.PermissionDenied, "Thu ngân chỉ được kiểm tra món của nhà hàng được phân công."));
            }

            return;
        }

        if (!settings.Value.MenuReaderRoleIds.Concat(settings.Value.MenuManagerRoleIds)
            .Contains(reply.Result.RoleId, StringComparer.Ordinal))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Bạn không có quyền xem thực đơn."));
        }
    }
}
