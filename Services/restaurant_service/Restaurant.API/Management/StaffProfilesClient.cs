using Backend.Contracts.Identity;
using Grpc.Core;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;
using Restaurant.Common.Exceptions;

namespace Restaurant.API.Management;

public sealed class StaffProfilesClient(UserGrpc.UserGrpcClient client, IConfiguration configuration) : IStaffProfiles
{
    public async Task FillAsync(Guid restaurantId, IReadOnlyList<RestaurantStaffDto> assignments, CancellationToken ct)
    {
        var key = configuration["StaffProvisioning:ServiceKey"];
        if (string.IsNullOrEmpty(key) || key.Length < 32)
        {
            throw new ManagementException(503, "Chưa cấu hình khóa tra cứu thông tin nhân viên.");
        }

        foreach (var batch in assignments.Chunk(100))
        {
            var request = new StaffProfilesRequest
            {
                RestaurantId = restaurantId.ToString()
            };
            request.UserIds.AddRange(batch.Select(item => item.UserIdRef.ToString()));
            var reply = await client.GetStaffProfilesAsync(request,
                new Metadata { { "x-staff-provisioning-key", key } }, DateTime.UtcNow.AddSeconds(5), ct);
            if (!reply.IsSuccess)
            {
                throw new ManagementException(503, "Chưa lấy được thông tin nhân viên.");
            }

            var profiles = reply.Items.ToDictionary(item => item.UserId);
            foreach (var assignment in batch)
            {
                if (profiles.TryGetValue(assignment.UserIdRef.ToString(), out var profile))
                {
                    assignment.FullName = profile.HasFullName ? profile.FullName : null;
                    assignment.UserName = profile.UserName;
                    assignment.Email = profile.Email;
                    assignment.RoleId = profile.RoleId;
                    assignment.AvatarUrl = profile.HasAvatarUrl ? profile.AvatarUrl : null;
                }
            }
        }
    }
}
