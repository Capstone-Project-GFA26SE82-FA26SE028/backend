using ApiGateway.DTOs.Customer;
using Backend.Contracts.Customer;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Controllers.Customer;

[ApiController]
[Route("api/feedbacks")]
public sealed class FeedbackController(Backend.Contracts.Customer.FeedbackGrpc.FeedbackGrpcClient client) : CustomerControllerBase
{
    [HttpPost]
    public Task<IActionResult> Submit([FromBody] SubmitFeedbackRequestDTO request, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var rpcRequest = new SubmitFeedbackRequest
            {
                OrderRef = request.OrderRef.ToString(),
                AccessToken = request.AccessToken ?? string.Empty,
                CustomerId = request.CustomerId?.ToString() ?? string.Empty,
                Star = request.Star,
                Comment = request.Comment ?? string.Empty
            };

            if (request.Items is not null)
            {
                foreach (var item in request.Items)
                {
                    rpcRequest.Items.Add(new FeedbackItemInfo
                    {
                        OrderItemRef = item.OrderItemRef.ToString(),
                        Star = item.Star,
                        Comment = item.Comment ?? string.Empty
                    });
                }
            }

            var reply = await client.SubmitAsync(rpcRequest, Options(ct));
            return MapFeedback(reply);
        }, ct, 201);
    }

    [HttpPost("access")]
    public Task<IActionResult> CreateAccess([FromBody] CreateFeedbackAccessDTO request, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.CreateAccessAsync(new FeedbackAccessRequest
            {
                OrderRef = request.OrderRef.ToString()
            }, Options(ct));

            return new { AccessToken = reply.AccessToken };
        }, ct);
    }

    [HttpGet("orders/{orderRef:guid}")]
    public Task<IActionResult> GetByOrder(Guid orderRef, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.GetByOrderAsync(new FeedbackOrderRequest
            {
                OrderRef = orderRef.ToString()
            }, Options(ct));

            return MapFeedback(reply);
        }, ct);
    }

    [HttpGet]
    public Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] int? star = null, CancellationToken ct = default)
    {
        return Execute(async () =>
        {
            var rpcRequest = new FeedbackListRequest
            {
                Page = page,
                PageSize = pageSize
            };

            if (star.HasValue)
            {
                rpcRequest.Star = star.Value;
            }

            var reply = await client.GetAllAsync(rpcRequest, Options(ct));
            return new
            {
                Items = reply.Items.Select(MapFeedback).ToList(),
                TotalCount = reply.TotalCount,
                Page = page,
                PageSize = pageSize
            };
        }, ct);
    }

    [HttpGet("summary")]
    public Task<IActionResult> GetSummary(CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.GetSummaryAsync(new Empty(), Options(ct));
            return new
            {
                AverageRating = reply.AverageRating,
                TotalFeedbacks = reply.TotalFeedbacks,
                StarDistribution = new
                {
                    Star5 = reply.Star5Count,
                    Star4 = reply.Star4Count,
                    Star3 = reply.Star3Count,
                    Star2 = reply.Star2Count,
                    Star1 = reply.Star1Count
                }
            };
        }, ct);
    }

    private static object MapFeedback(FeedbackInfo info)
    {
        return new
        {
            FeedbackId = info.FeedbackId,
            OrderRef = info.OrderRef,
            CustomerId = string.IsNullOrEmpty(info.CustomerId) ? null : info.CustomerId,
            CustomerName = info.CustomerName,
            Star = info.Star,
            Comment = info.Comment,
            SubmittedAt = info.SubmittedAt,
            Items = info.Items.Select(i => new
            {
                OrderItemRef = i.OrderItemRef,
                Star = i.Star,
                Comment = i.Comment
            }).ToList()
        };
    }
}

