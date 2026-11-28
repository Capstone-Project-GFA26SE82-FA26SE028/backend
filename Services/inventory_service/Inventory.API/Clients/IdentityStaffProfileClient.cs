using Backend.Contracts.Identity;
using Grpc.Core;
using Inventory.BLL.Interfaces;

namespace Inventory.API.Clients;

public sealed class IdentityStaffProfileClient(UserGrpc.UserGrpcClient client, IConfiguration configuration)
    : IInventoryStaffProfileClient
{
    public async Task<string?> GetFullNameAsync(Guid restaurantId, Guid userId, CancellationToken ct)
    {
        if (restaurantId == Guid.Empty || userId == Guid.Empty)
        {
            return null;
        }

        var key = configuration["StaffProvisioning:ServiceKey"];
        if (string.IsNullOrEmpty(key) || key.Length < 32)
        {
            return null;
        }

        try
        {
            var request = new StaffProfilesRequest
            {
                RestaurantId = restaurantId.ToString()
            };
            request.UserIds.Add(userId.ToString());
            var reply = await client.GetStaffProfilesAsync(
                request,
                new Metadata
                {
                    { "x-staff-provisioning-key", key }
                },
                DateTime.UtcNow.AddSeconds(5),
                ct);
            if (!reply.IsSuccess)
            {
                return null;
            }

            var profile = reply.Items.FirstOrDefault(item =>
                string.Equals(item.UserId, userId.ToString(), StringComparison.OrdinalIgnoreCase));
            if (profile is null || !profile.HasFullName || string.IsNullOrWhiteSpace(profile.FullName))
            {
                return null;
            }

            return profile.FullName.Trim();
        }
        catch (RpcException)
        {
            return null;
        }
    }
}
