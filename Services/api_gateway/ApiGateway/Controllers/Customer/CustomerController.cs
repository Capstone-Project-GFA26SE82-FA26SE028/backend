using ApiGateway.DTOs.Customer;
using Backend.Contracts.Customer;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Controllers.Customer;

[ApiController]
[Route("api/customers")]
public sealed class CustomerController(Backend.Contracts.Customer.CustomerGrpc.CustomerGrpcClient client) : CustomerControllerBase
{
    [HttpPost]
    public Task<IActionResult> Create([FromBody] CreateCustomerRequestDTO request, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.GetOrCreateAsync(new CreateCustomerRequest
            {
                Phone = request.PhoneNumber ?? string.Empty,
                Name = request.CustomerName ?? string.Empty
            }, Options(ct));

            return MapCustomer(reply);
        }, ct, 201);
    }

    [HttpGet("by-phone/{phone}")]
    public Task<IActionResult> GetByPhone(string phone, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.GetByPhoneAsync(new PhoneRequest
            {
                Phone = phone
            }, Options(ct));

            return MapCustomer(reply);
        }, ct);
    }

    [HttpGet("{id:guid}")]
    public Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.GetByIdAsync(new CustomerIdRequest
            {
                CustomerId = id.ToString()
            }, Options(ct));

            return MapCustomer(reply);
        }, ct);
    }

    [HttpGet("/api/loyalty/tiers")]
    public Task<IActionResult> GetTiers(CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.GetTiersAsync(new Empty(), Options(ct));
            return reply.Items.Select(t => new
            {
                LoyaltyTierId = t.LoyaltyTierId,
                LoyaltyTierCode = t.LoyaltyTierCode,
                LoyaltyTierName = t.LoyaltyTierName,
                RequiredSpend = decimal.TryParse(t.RequiredSpend, out var req) ? req : 0m,
                DiscountPercent = decimal.TryParse(t.DiscountPercent, out var disc) ? disc : 0m
            });
        }, ct);
    }

    [HttpPost("/api/loyalty/earn")]
    public Task<IActionResult> EarnPoints([FromBody] EarnPointsRequestDTO request, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.EarnPointsAsync(new EarnPointsRequest
            {
                CustomerId = request.CustomerId.ToString(),
                BillId = request.BillId.ToString(),
                FinalAmount = request.FinalAmount.ToString("0.##")
            }, Options(ct));

            return new
            {
                PointsEarned = reply.PointsEarned,
                NewTotalPoints = reply.NewTotalPoints,
                LoyaltyTierName = reply.LoyaltyTierName,
                TierUpgraded = reply.TierUpgraded
            };
        }, ct);
    }

    [HttpPost("/api/loyalty/reserve")]
    public Task<IActionResult> ReservePoints([FromBody] ReservePointsRequestDTO request, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.ReservePointsAsync(new ReservePointsRequest
            {
                CustomerId = request.CustomerId.ToString(),
                BillId = request.BillId.ToString(),
                Points = request.Points
            }, Options(ct));

            return new
            {
                PointReservationId = reply.PointReservationId,
                ReservedPoints = reply.ReservedPoints,
                DiscountAmount = decimal.TryParse(reply.DiscountAmount, out var disc) ? disc : 0m,
                RemainingPoints = reply.RemainingPoints
            };
        }, ct);
    }

    [HttpPost("/api/loyalty/commit")]
    public Task<IActionResult> CommitPoints([FromBody] BillPointsRequestDTO request, CancellationToken ct)
    {
        return Execute(async () =>
        {
            await client.CommitPointsAsync(new BillIdRequest
            {
                BillId = request.BillId.ToString()
            }, Options(ct));

            return new { Message = "Đã xác nhận sử dụng điểm thành công." };
        }, ct);
    }

    [HttpPost("/api/loyalty/cancel")]
    public Task<IActionResult> CancelPoints([FromBody] BillPointsRequestDTO request, CancellationToken ct)
    {
        return Execute(async () =>
        {
            await client.CancelPointsAsync(new BillIdRequest
            {
                BillId = request.BillId.ToString()
            }, Options(ct));

            return new { Message = "Đã hủy giữ điểm." };
        }, ct);
    }

    private static object MapCustomer(CustomerInfo info)
    {
        return new
        {
            CustomerId = info.CustomerId,
            NormalizedPhone = info.NormalizedPhone,
            CustomerName = info.CustomerName,
            LoyaltyTierId = info.LoyaltyTierId,
            LoyaltyTierName = info.LoyaltyTierName,
            LoyaltyTierCode = info.LoyaltyTierCode,
            DiscountPercent = decimal.TryParse(info.DiscountPercent, out var disc) ? disc : 0m,
            AvailablePoints = info.AvailablePoints,
            TotalSpent = decimal.TryParse(info.TotalSpent, out var spent) ? spent : 0m
        };
    }
}

