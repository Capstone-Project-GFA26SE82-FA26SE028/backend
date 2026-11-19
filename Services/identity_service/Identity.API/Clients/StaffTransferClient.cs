using Backend.Contracts.Restaurant;
using Grpc.Core;
using Identity.BLL.Interfaces;
using Identity.Common.DTOs;

namespace Identity.API.Clients;

public sealed class StaffTransferClient(StaffTransferReceiptGrpc.StaffTransferReceiptGrpcClient client,
    IConfiguration configuration) : IStaffTransferRemote
{
    public bool IsConfigured => configuration["StaffProvisioning:ServiceKey"]?.Length >= 32;

    public async Task<StaffTransferResult> ApplyAsync(Guid id, Guid userId, Guid source, Guid target, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            return new("Pending", null);
        }

        try
        {
            var reply = await client.ApplyAsync(new StaffAssignmentTransferRequest
            {
                TransferId = id.ToString(),
                UserId = userId.ToString(),
                SourceRestaurantId = source.ToString(),
                TargetRestaurantId = target.ToString()
            }, new Metadata { { "x-staff-provisioning-key", configuration["StaffProvisioning:ServiceKey"]! } },
                DateTime.UtcNow.AddSeconds(10), ct);
            return new(reply.Status, string.IsNullOrEmpty(reply.Message) ? null : reply.Message);
        }
        catch (RpcException) when (!ct.IsCancellationRequested)
        {
            return new("Pending", null);
        }
    }
}
