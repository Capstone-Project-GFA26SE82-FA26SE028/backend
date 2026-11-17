using Backend.Contracts.Restaurant;
using Grpc.Core;
using Identity.BLL.Interfaces;

namespace Identity.API.Clients;

public sealed class RestaurantStaffClient(RestaurantAccessGrpc.RestaurantAccessGrpcClient access,
    RestaurantStaffGrpc.RestaurantStaffGrpcClient staff, IHttpContextAccessor accessor,
    IConfiguration configuration) : IUserRestaurantAccess, IStaffProvisioner
{
    public bool IsConfigured => configuration["StaffProvisioning:ServiceKey"]?.Length >= 32;

    public async Task<bool> CanManageAsync(Guid actorId, Guid restaurantId, CancellationToken ct)
    {
        var authorization = accessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            return false;
        }

        try
        {
            var reply = await access.GetCurrentScopeAsync(new CurrentRestaurantScopeRequest
            {
                RestaurantId = restaurantId.ToString()
            }, new Metadata { { "authorization", authorization } }, DateTime.UtcNow.AddSeconds(5), ct);
            return reply.UserId == actorId.ToString() && (reply.IsAdministrator
                || (reply.RoleId == "admin" && reply.RestaurantIds.Contains(restaurantId.ToString())));
        }
        catch (RpcException)
        {
            return false;
        }
    }

    public async Task<bool> EnsureAssignmentAsync(Guid restaurantId, Guid userId, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            return false;
        }

        try
        {
            var reply = await staff.ProvisionAsync(new RestaurantStaffRequest
            {
                RestaurantId = restaurantId.ToString(),
                UserIdRef = userId.ToString()
            }, new Metadata { { "x-staff-provisioning-key", configuration["StaffProvisioning:ServiceKey"]! } },
                DateTime.UtcNow.AddSeconds(5), ct);
            return reply.IsActive && reply.RestaurantId == restaurantId.ToString() && reply.UserIdRef == userId.ToString();
        }
        catch (RpcException)
        {
            return false;
        }
    }
}
