using Backend.Contracts.Ordering;
using Grpc.Core;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.Exceptions;

namespace Restaurant.API.Management;

public sealed class StaffTransferGuardClient(StaffTransferGuardGrpc.StaffTransferGuardGrpcClient client,
    IConfiguration configuration) : IStaffTransferGuard
{
    public async Task<bool> HasKitchenAssignmentAsync(Guid userId, CancellationToken ct)
    {
        var key = configuration["Notifications:ServiceKey"];
        if (string.IsNullOrEmpty(key) || key.Length < 32)
        {
            throw new ManagementException(503, "Chưa cấu hình kiểm tra phân công bếp.");
        }

        var reply = await client.CheckAsync(new StaffTransferGuardRequest
        {
            UserId = userId.ToString()
        }, new Metadata { { "x-notification-key", key } }, DateTime.UtcNow.AddSeconds(5), ct);
        return reply.HasKitchenAssignment;
    }
}
