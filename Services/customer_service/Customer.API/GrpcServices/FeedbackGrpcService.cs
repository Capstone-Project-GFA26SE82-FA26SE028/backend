using Backend.Contracts.Customer;
using Customer.BLL.Interfaces;
using Customer.Common.DTOs;
using Customer.Common.Exceptions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Customer.API.GrpcServices;

public sealed class FeedbackGrpcService(IFeedbackService service) : Backend.Contracts.Customer.FeedbackGrpc.FeedbackGrpcBase
{
    public override async Task<FeedbackAccessReply> CreateAccess(FeedbackAccessRequest request, ServerCallContext context)
    {
        try
        {
            if (!Guid.TryParse(request.OrderRef, out var orderRef))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid order reference."));
            }

            var token = await service.CreateFeedbackAccess(orderRef, context.CancellationToken);
            return new FeedbackAccessReply
            {
                AccessToken = token
            };
        }
        catch (CustomerException ex)
        {
            throw new RpcException(new Status(MapStatusCode(ex.StatusCode), ex.Message));
        }
    }

    public override async Task<FeedbackInfo> Submit(SubmitFeedbackRequest request, ServerCallContext context)
    {
        try
        {
            if (!Guid.TryParse(request.OrderRef, out var orderRef))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid order reference."));
            }

            Guid? customerId = null;
            if (!string.IsNullOrEmpty(request.CustomerId) && Guid.TryParse(request.CustomerId, out var cid))
            {
                customerId = cid;
            }

            var items = new List<FeedbackItemDTO>();
            if (request.Items is not null)
            {
                foreach (var item in request.Items)
                {
                    if (Guid.TryParse(item.OrderItemRef, out var itemId))
                    {
                        items.Add(new FeedbackItemDTO
                        {
                            OrderItemRef = itemId,
                            Star = item.Star,
                            Comment = item.Comment
                        });
                    }
                }
            }

            var result = await service.SubmitFeedback(new SubmitFeedbackDTO
            {
                OrderRef = orderRef,
                AccessToken = request.AccessToken,
                CustomerId = customerId,
                Star = request.Star,
                Comment = request.Comment,
                Items = items
            }, context.CancellationToken);

            return CustomerMapping.Map(result);
        }
        catch (CustomerException ex)
        {
            throw new RpcException(new Status(MapStatusCode(ex.StatusCode), ex.Message));
        }
    }

    public override async Task<FeedbackInfo> GetByOrder(FeedbackOrderRequest request, ServerCallContext context)
    {
        try
        {
            if (!Guid.TryParse(request.OrderRef, out var orderRef))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid order reference."));
            }

            var feedback = await service.GetByOrder(orderRef, context.CancellationToken);
            if (feedback is null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, "Feedback not found for this order."));
            }

            return CustomerMapping.Map(feedback);
        }
        catch (CustomerException ex)
        {
            throw new RpcException(new Status(MapStatusCode(ex.StatusCode), ex.Message));
        }
    }

    public override async Task<FeedbackListReply> GetAll(FeedbackListRequest request, ServerCallContext context)
    {
        try
        {
            var (items, total) = await service.GetAll(
                request.Page,
                request.PageSize,
                request.HasStar ? request.Star : null,
                context.CancellationToken);

            var reply = new FeedbackListReply
            {
                TotalCount = total
            };

            reply.Items.AddRange(items.Select(CustomerMapping.Map));
            return reply;
        }
        catch (CustomerException ex)
        {
            throw new RpcException(new Status(MapStatusCode(ex.StatusCode), ex.Message));
        }
    }

    public override async Task<FeedbackSummaryReply> GetSummary(Empty request, ServerCallContext context)
    {
        var summary = await service.GetSummary(context.CancellationToken);
        return new FeedbackSummaryReply
        {
            AverageRating = summary.AverageRating,
            TotalFeedbacks = summary.TotalFeedbacks,
            Star5Count = summary.Star5Count,
            Star4Count = summary.Star4Count,
            Star3Count = summary.Star3Count,
            Star2Count = summary.Star2Count,
            Star1Count = summary.Star1Count
        };
    }

    private static StatusCode MapStatusCode(int code)
    {
        return code switch
        {
            400 => StatusCode.InvalidArgument,
            401 => StatusCode.Unauthenticated,
            403 => StatusCode.PermissionDenied,
            404 => StatusCode.NotFound,
            409 => StatusCode.AlreadyExists,
            _ => StatusCode.Internal
        };
    }
}

