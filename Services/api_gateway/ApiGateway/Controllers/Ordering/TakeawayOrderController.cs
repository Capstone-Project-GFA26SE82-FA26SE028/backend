using ApiGateway.DTOs.Ordering;
using ApiGateway.Settings;
using ApiGateway.Swagger;
using Backend.Contracts.Ordering;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;

namespace ApiGateway.Controllers.Ordering;

[ApiController]
[Route("api/restaurants/{restaurantId:guid}/takeaway-orders")]
[RequireBearerToken]
public sealed class TakeawayOrderController(OrderGrpc.OrderGrpcClient client, IOptions<OrderingClientSettings> settings)
    : OrderingControllerBase(settings)
{
    [HttpPost]
    public Task<IActionResult> Create(Guid restaurantId, CreateTakeawayDTO body,
        [FromHeader(Name = "Idempotency-Key")] string key, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var request = new TakeawayCreateRequest
            {
                RestaurantId = restaurantId.ToString(),
                IdempotencyKey = key,
                CartId = body.CartId.ToString(),
                CartVersion = body.CartVersion ?? "",
                PickupName = body.PickupName ?? "",
                PickupPhone = body.PickupPhone ?? "",
            };
            var reply = await client.CreateTakeawayAsync(request, Options(ct));
            if (reply.Created)
            {
                Response.Headers.Location = $"/api/restaurants/{restaurantId}/takeaway-orders/{reply.Result.OrderId}";
            }

            return Result(OrderingMapping.Map(reply.Result), reply.Created ? 201 : 200);
        }, ct);
    }

    [HttpGet]
    public Task<IActionResult> GetAll(Guid restaurantId, CancellationToken ct,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? status = null)
    {
        return Execute(async () =>
        {
            var request = new TakeawayListRequest
            {
                RestaurantId = restaurantId.ToString(),
                Page = page,
                PageSize = pageSize
            };
            if (status is not null)
            {
                request.Status = status;
            }

            var reply = await client.GetAllTakeawayAsync(request, Options(ct));
            return Result(reply.Items.Select(OrderingMapping.Map).ToList());
        }, ct);
    }

    [HttpGet("{id:guid}")]
    public Task<IActionResult> GetById(Guid restaurantId, Guid id, CancellationToken ct)
    {
        return Execute(async () => Result(OrderingMapping.Map(await client.GetTakeawayByIdAsync(new TakeawayIdRequest
        {
            RestaurantId = restaurantId.ToString(),
            OrderId = id.ToString()
        }, Options(ct)))), ct);
    }

    [HttpPost("{id:guid}/collect")]
    public Task<IActionResult> Collect(Guid restaurantId, Guid id, TakeawayActionDTO body, CancellationToken ct)
    {
        return Execute(async () => Result(OrderingMapping.Map(await client.CollectTakeawayAsync(new TakeawayIdRequest
        {
            RestaurantId = restaurantId.ToString(),
            OrderId = id.ToString(),
            Version = body.Version ?? "",
            Reason = body.Reason ?? "",
            PickupCode = body.PickupCode ?? "",
            HandoverConfirmed = body.HandoverConfirmed,
            RefundReference = body.RefundReference ?? "",
            RefundEvidence = body.RefundEvidence ?? ""
        }, Options(ct)))), ct);
    }

    [HttpPost("{id:guid}/cancel")]
    public Task<IActionResult> Cancel(Guid restaurantId, Guid id, TakeawayActionDTO body, CancellationToken ct)
    {
        return Execute(async () => Result(OrderingMapping.Map(await client.CancelTakeawayAsync(new TakeawayIdRequest
        {
            RestaurantId = restaurantId.ToString(),
            OrderId = id.ToString(),
            Version = body.Version ?? "",
            Reason = body.Reason ?? "",
            PickupCode = body.PickupCode ?? "",
            HandoverConfirmed = body.HandoverConfirmed,
            RefundReference = body.RefundReference ?? "",
            RefundEvidence = body.RefundEvidence ?? ""
        }, Options(ct)))), ct);
    }

    [HttpPost("{id:guid}/accept")]
    public Task<IActionResult> Accept(Guid restaurantId, Guid id, TakeawayActionDTO body, CancellationToken ct)
    {
        return Execute(async () => Result(OrderingMapping.Map(await client.AcceptTakeawayAsync(new TakeawayIdRequest
        {
            RestaurantId = restaurantId.ToString(),
            OrderId = id.ToString(),
            Version = body.Version ?? "",
            Reason = body.Reason ?? "",
            PickupCode = body.PickupCode ?? "",
            HandoverConfirmed = body.HandoverConfirmed,
            RefundReference = body.RefundReference ?? "",
            RefundEvidence = body.RefundEvidence ?? ""
        }, Options(ct)))), ct);
    }

    [HttpPost("{id:guid}/reject")]
    public Task<IActionResult> Reject(Guid restaurantId, Guid id, TakeawayActionDTO body, CancellationToken ct)
    {
        return Execute(async () => Result(OrderingMapping.Map(await client.RejectTakeawayAsync(new TakeawayIdRequest
        {
            RestaurantId = restaurantId.ToString(),
            OrderId = id.ToString(),
            Version = body.Version ?? "",
            Reason = body.Reason ?? "",
            PickupCode = body.PickupCode ?? "",
            HandoverConfirmed = body.HandoverConfirmed,
            RefundReference = body.RefundReference ?? "",
            RefundEvidence = body.RefundEvidence ?? ""
        }, Options(ct)))), ct);
    }

    [HttpPost("{id:guid}/refund/verify")]
    public Task<IActionResult> VerifyRefund(Guid restaurantId, Guid id, TakeawayActionDTO body, CancellationToken ct)
    {
        return Execute(async () => Result(OrderingMapping.Map(await client.VerifyTakeawayRefundAsync(new TakeawayIdRequest
        {
            RestaurantId = restaurantId.ToString(),
            OrderId = id.ToString(),
            Version = body.Version ?? "",
            Reason = body.Reason ?? "",
            PickupCode = body.PickupCode ?? "",
            HandoverConfirmed = body.HandoverConfirmed,
            RefundReference = body.RefundReference ?? "",
            RefundEvidence = body.RefundEvidence ?? ""
        }, Options(ct)))), ct);
    }

}
