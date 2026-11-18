using System.Globalization;
using Backend.Contracts.Restaurant;
using Grpc.Core;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;
using Restaurant.Common.Exceptions;

namespace Restaurant.API.GrpcServices;

public sealed class ServiceRequestGrpcService(IServiceRequestService service) : ServiceRequestGrpc.ServiceRequestGrpcBase
{
    public override async Task<PaymentServiceRequestInfo> Create(PaymentServiceRequest request, ServerCallContext context)
    {
        return Map(await service.Create(ParseId(request.TableSessionId), ParseId(request.ServiceRequestId),
            request.GuestToken, context.CancellationToken));
    }

    public override async Task<PaymentServiceRequestInfo> GetById(PaymentServiceRequest request, ServerCallContext context)
    {
        return Map(await service.GetById(ParseId(request.TableSessionId), ParseId(request.ServiceRequestId),
            request.GuestToken, context.CancellationToken));
    }

    public override async Task<PaymentServiceRequestInfo> GetCurrent(GuestAccessRequest request, ServerCallContext context)
    {
        return Map(await service.GetCurrent(ParseId(request.TableSessionId), request.Token, context.CancellationToken));
    }

    public override async Task<PaymentServiceRequestPage> GetAll(PaymentServiceRequestList request, ServerCallContext context)
    {
        var result = await service.GetAll(ParseId(request.RestaurantId), request.Page,
            request.PageSize, context.CancellationToken);
        var reply = new PaymentServiceRequestPage
        {
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize
        };
        reply.Items.AddRange(result.Items.Select(Map));
        return reply;
    }

    public override async Task<PaymentServiceRequestInfo> Acknowledge(PaymentServiceRequest request, ServerCallContext context)
    {
        return Map(await service.Acknowledge(ParseId(request.TableSessionId), ParseId(request.ServiceRequestId),
            context.CancellationToken));
    }

    public override async Task<PaymentRequestReviewInfo> GetReview(PaymentServiceRequest request, ServerCallContext context)
    {
        var result = await service.GetReview(ParseId(request.TableSessionId), ParseId(request.ServiceRequestId), context.CancellationToken);
        var reply = new PaymentRequestReviewInfo
        {
            RestaurantId = result.RestaurantId.ToString(),
            PendingOrderCount = result.PendingOrderCount
        };
        reply.Items.AddRange(result.Items.Select(item => new PaymentRequestReviewItem
        {
            OrderItemId = item.OrderItemId.ToString(),
            TableId = item.TableId.ToString(),
            Name = item.Name,
            Quantity = item.Quantity,
            Status = item.Status
        }));
        return reply;
    }

    public override async Task<PaymentResultAccessInfo> GetResultAccess(PaymentServiceRequest request, ServerCallContext context)
    {
        var restaurantId = await service.GetResultRestaurant(ParseId(request.TableSessionId),
            ParseId(request.ServiceRequestId), context.CancellationToken);
        return new PaymentResultAccessInfo
        {
            RestaurantId = restaurantId.ToString()
        };
    }

    private static Guid ParseId(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new ManagementException(400, "Yêu cầu mã nhà hàng, phiên bàn và yêu cầu thanh toán hợp lệ.");
        }

        return id;
    }

    private static PaymentServiceRequestInfo Map(ServiceRequestDto request)
    {
        var reply = new PaymentServiceRequestInfo
        {
            ServiceRequestId = request.ServiceRequestId.ToString(),
            TableSessionId = request.TableSessionId.ToString(),
            RestaurantId = request.RestaurantId.ToString(),
            Status = request.Status,
            RequestedAt = request.RequestedAt.ToString("O", CultureInfo.InvariantCulture)
        };
        if (request.AcknowledgedAt.HasValue)
        {
            reply.AcknowledgedAt = request.AcknowledgedAt.Value.ToString("O", CultureInfo.InvariantCulture);
        }

        if (request.HandledByRef.HasValue)
        {
            reply.HandledByRef = request.HandledByRef.Value.ToString();
        }

        reply.Tables.AddRange(request.Tables.Select(table => new PaymentServiceRequestTable
        {
            TableId = table.TableId.ToString(),
            TableCode = table.TableCode
        }));
        return reply;
    }
}
