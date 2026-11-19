using Backend.Contracts.Ordering;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Ordering.BLL.Interfaces;
using Ordering.Common.DTOs;
using Ordering.Common.Exceptions;

namespace Ordering.API.GrpcServices;

public sealed class KitchenAssignmentGrpcService(IKitchenAssignmentService service) : KitchenAssignmentGrpc.KitchenAssignmentGrpcBase
{
    public override async Task<KitchenAssignmentPage> GetAll(KitchenAssignmentListRequest request, ServerCallContext context)
    {
        var page = await service.GetAll(request.Page, request.PageSize, context.CancellationToken);
        var reply = new KitchenAssignmentPage
        {
            TotalCount = page.TotalCount,
            Page = page.Page,
            PageSize = page.PageSize
        };
        reply.Items.AddRange(page.Items.Select(Map));
        return reply;
    }

    public override async Task<KitchenEmployeeList> GetEmployees(Empty request, ServerCallContext context)
    {
        var reply = new KitchenEmployeeList();
        reply.Items.AddRange((await service.GetEmployees(context.CancellationToken)).Select(Map));
        return reply;
    }

    public override async Task<KitchenAssignmentInfo> GetById(KitchenAssignmentRequest request, ServerCallContext context)
    {
        return Map(await service.GetById(Id(request.StationId), context.CancellationToken));
    }

    public override async Task<KitchenAssignmentInfo> Create(KitchenAssignmentRequest request, ServerCallContext context)
    {
        return Map(await service.Create(Id(request.StationId), Id(request.UserId), context.CancellationToken));
    }

    public override async Task<KitchenAssignmentInfo> Update(KitchenAssignmentRequest request, ServerCallContext context)
    {
        return Map(await service.Update(Id(request.StationId), Id(request.UserId), Id(request.ExpectedUserId), context.CancellationToken));
    }

    public override async Task<Empty> Delete(KitchenAssignmentRequest request, ServerCallContext context)
    {
        await service.Delete(Id(request.StationId), Id(request.ExpectedUserId), context.CancellationToken);
        return new Empty();
    }

    private static Guid Id(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new OrderingException(400, "Mã trạm bếp hoặc nhân viên không hợp lệ.");
        }

        return id;
    }

    private static KitchenAssignmentInfo Map(KitchenAssignmentDTO item)
    {
        var result = new KitchenAssignmentInfo
        {
            KitchenStationId = item.KitchenStationId.ToString(),
            KitchenStationCode = item.KitchenStationCode,
            KitchenStationName = item.KitchenStationName,
            StationActive = item.StationActive,
            UserId = item.UserId.ToString()
        };
        if (item.Employee is not null)
        {
            result.Employee = Map(item.Employee);
        }

        return result;
    }

    private static KitchenEmployeeInfo Map(KitchenEmployeeDTO item)
    {
        return new KitchenEmployeeInfo
        {
            UserId = item.UserId.ToString(),
            FullName = item.FullName ?? "",
            UserName = item.UserName ?? "",
            Email = item.Email ?? "",
            AvatarUrl = item.AvatarUrl ?? "",
            RoleId = item.RoleId,
            IsActive = item.IsActive
        };
    }
}
