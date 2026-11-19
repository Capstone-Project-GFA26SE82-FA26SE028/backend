using Backend.Contracts.Identity;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Identity.BLL.Interfaces;
using Identity.Common.DTOs;
using Shared.Common.DTOs;

namespace Identity.API.GrpcServices;

public sealed class StaffTransferGrpcService(IStaffTransferService service) : StaffTransferGrpc.StaffTransferGrpcBase
{
    public override async Task<StaffTransferReply> Create(CreateStaffTransferRequest request, ServerCallContext context)
    {
        var actor = await GrpcAuthentication.RequireUser(context);
        return Map(await service.Create(actor.UserId, Parse(request.TransferId), Parse(request.UserId),
            Parse(request.TargetRestaurantId), context.CancellationToken));
    }

    public override async Task<StaffTransferReply> GetById(StaffTransferIdRequest request, ServerCallContext context)
    {
        var actor = await GrpcAuthentication.RequireUser(context);
        return Map(await service.GetById(actor.UserId, Parse(request.TransferId), context.CancellationToken));
    }

    private static Guid Parse(string value)
    {
        return Guid.TryParse(value, out var id) && id != Guid.Empty ? id
            : throw new RpcException(new Status(StatusCode.InvalidArgument, "Mã chuyển nhân viên không hợp lệ."));
    }

    private static StaffTransferReply Map(ResponseDTO response)
    {
        var reply = new StaffTransferReply
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? "",
            IsSuccess = response.IsSuccess
        };
        if (response.Result is StaffTransferDTO item)
        {
            reply.Result = new StaffTransferInfo
            {
                Id = item.Id.ToString(),
                UserId = item.UserId.ToString(),
                SourceRestaurantId = item.SourceRestaurantId.ToString(),
                TargetRestaurantId = item.TargetRestaurantId.ToString(),
                Status = item.Status,
                Message = item.Message ?? "",
                CreatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(item.CreatedAt, DateTimeKind.Utc)),
                CompletedAt = item.CompletedAt.HasValue
                    ? Timestamp.FromDateTime(DateTime.SpecifyKind(item.CompletedAt.Value, DateTimeKind.Utc)) : null
            };
        }

        return reply;
    }
}
