using Backend.Contracts.Ordering;
using Grpc.Core;
using Ordering.BLL.Interfaces;
using Ordering.Common.DTOs;
using Ordering.Common.Exceptions;

namespace Ordering.API.GrpcServices;

public sealed class CancellationRequestGrpcService(ICancellationRequestService service) : CancellationRequestGrpc.CancellationRequestGrpcBase
{
    public override async Task<CancellationCreateReply> Create(CancellationWriteRequest request, ServerCallContext context)
    {
        var result = await service.Create(Id(request.Access?.TableSessionId), Id(request.IdempotencyKey),
            Id(request.OrderItemId), request.Quantity, request.Reason, request.Access?.GuestToken ?? "", context.CancellationToken);
        return new CancellationCreateReply
        {
            Result = Map(result.Result),
            Created = result.Created
        };
    }

    public override async Task<CancellationCreateReply> CancelByStaff(CancellationWriteRequest request, ServerCallContext context)
    {
        var result = await service.CancelByStaff(Id(request.Access?.TableSessionId), Id(request.IdempotencyKey),
            Id(request.OrderItemId), request.Quantity, request.Reason, context.CancellationToken);
        return new CancellationCreateReply
        {
            Result = Map(result.Result),
            Created = result.Created
        };
    }

    public override async Task<CancellationInfo> GetById(CancellationDecisionRequest request, ServerCallContext context)
    {
        return Map(await service.GetById(Id(request.Access?.TableSessionId), Id(request.Id),
            request.Access?.GuestToken ?? "", context.CancellationToken));
    }

    public override async Task<CancellationPage> GetAll(CancellationListRequest request, ServerCallContext context)
    {
        var result = await service.GetAll(string.IsNullOrEmpty(request.Access?.TableSessionId)
                ? null : Id(request.Access.TableSessionId), request.Status, request.Page, request.PageSize,
            request.Access?.GuestToken ?? "", context.CancellationToken);
        var reply = new CancellationPage
        {
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize
        };
        reply.Items.AddRange(result.Items.Select(Map));
        return reply;
    }

    public override async Task<CancellationInfo> Approve(CancellationDecisionRequest request, ServerCallContext context)
    {
        return Map(await service.Approve(Id(request.Access?.TableSessionId), Id(request.Id), context.CancellationToken));
    }

    public override async Task<CancellationInfo> Reject(CancellationDecisionRequest request, ServerCallContext context)
    {
        return Map(await service.Reject(Id(request.Access?.TableSessionId), Id(request.Id), request.Reason, context.CancellationToken));
    }

    private static Guid Id(string? value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new OrderingException(400, "Mã yêu cầu, phiên bàn hoặc món không hợp lệ; Idempotency-Key phải là UUID.");
        }

        return id;
    }

    private static CancellationInfo Map(CancellationRequestDTO value)
    {
        var result = new CancellationInfo
        {
            CancellationRequestId = value.CancellationRequestId.ToString(),
            TableSessionId = value.TableSessionId.ToString(),
            OrderId = value.OrderId.ToString(),
            OrderNumber = value.OrderNumber,
            OrderItemId = value.OrderItemId.ToString(),
            ItemName = value.ItemName,
            Quantity = value.Quantity,
            Reason = value.Reason,
            Source = value.Source,
            RequestedByRef = value.RequestedByRef.ToString(),
            Status = value.Status,
            RequestedAt = DateTime.SpecifyKind(value.RequestedAt, DateTimeKind.Utc).ToString("O")
        };
        if (value.DecidedByRef.HasValue)
        {
            result.DecidedByRef = value.DecidedByRef.Value.ToString();
        }

        if (value.DecidedAt.HasValue)
        {
            result.DecidedAt = DateTime.SpecifyKind(value.DecidedAt.Value, DateTimeKind.Utc).ToString("O");
        }

        if (value.DecisionReason is not null)
        {
            result.DecisionReason = value.DecisionReason;
        }

        return result;
    }
}
