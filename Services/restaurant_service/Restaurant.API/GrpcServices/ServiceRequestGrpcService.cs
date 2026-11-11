using System.Globalization;
using Backend.Contracts.Restaurant;
using Grpc.Core;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;

namespace Restaurant.API.GrpcServices;

public sealed class ServiceRequestGrpcService(IServiceRequestService service) : ServiceRequestGrpc.ServiceRequestGrpcBase
{
    public override async Task<ServiceRequestInfo> Create(PaymentServiceRequest request, ServerCallContext context)
    {
        return Map(await service.Create(Id(request.TableSessionId), Id(request.ServiceRequestId), request.GuestToken, context.CancellationToken));
    }

    public override async Task<ServiceRequestInfo> GetById(PaymentServiceRequest request, ServerCallContext context)
    {
        return Map(await service.GetById(Id(request.TableSessionId), Id(request.ServiceRequestId), request.GuestToken, context.CancellationToken));
    }

    public override async Task<ServiceRequestPage> GetAll(ServiceRequestListRequest request, ServerCallContext context)
    {
        var result = await service.GetAll(Id(request.RestaurantId), request.HasStatus ? request.Status : null,
            request.Page, request.PageSize, context.CancellationToken);
        var reply = new ServiceRequestPage
        {
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize
        };
        reply.Items.AddRange(result.Items.Select(Map));
        return reply;
    }

    public override async Task<ServiceRequestInfo> Acknowledge(PaymentServiceRequest request, ServerCallContext context)
    {
        return Map(await service.Acknowledge(Id(request.TableSessionId), Id(request.ServiceRequestId), context.CancellationToken));
    }

    public override async Task<ServiceRequestInfo> Review(PaymentServiceRequest request, ServerCallContext context)
    {
        return Map(await service.Review(Id(request.TableSessionId), Id(request.ServiceRequestId), context.CancellationToken));
    }

    public override async Task<ServiceRequestInfo> Cancel(PaymentServiceRequest request, ServerCallContext context)
    {
        return Map(await service.Cancel(Id(request.TableSessionId), Id(request.ServiceRequestId), context.CancellationToken));
    }

    private static Guid Id(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Yêu cầu mã định danh hợp lệ."));
        }

        return id;
    }

    private static ServiceRequestInfo Map(ServiceRequestDto value)
    {
        var result = new ServiceRequestInfo
        {
            ServiceRequestId = value.ServiceRequestId.ToString(),
            TableSessionId = value.TableSessionId.ToString(),
            RestaurantId = value.RestaurantId.ToString(),
            Type = value.Type,
            Status = value.Status,
            RequestedAt = value.RequestedAt.ToString("O", CultureInfo.InvariantCulture)
        };
        if (value.AcknowledgedAt.HasValue)
        {
            result.AcknowledgedAt = value.AcknowledgedAt.Value.ToString("O", CultureInfo.InvariantCulture);
        }

        if (value.CompletedAt.HasValue)
        {
            result.CompletedAt = value.CompletedAt.Value.ToString("O", CultureInfo.InvariantCulture);
        }

        if (value.HandledByRef.HasValue)
        {
            result.HandledByRef = value.HandledByRef.Value.ToString();
        }

        return result;
    }
}
