using Backend.Contracts.Billing;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Grpc.Core;

namespace Billing.API.GrpcServices;

public sealed class CashRegisterGrpcService(ICashRegisterService service) : CashRegisterGrpc.CashRegisterGrpcBase
{
    public override async Task<CashRegisterInfo> Create(CreateCashRegisterRequest request, ServerCallContext context)
    {
        var result = await service.Create(new CreateCashRegisterDTO
        {
            RestaurantId = ParseId(request.RestaurantId, "restaurant"),
            Code = request.Code
        }, context.CancellationToken);
        return Map(result);
    }

    public override async Task<CashRegisterPage> GetAll(CashRegisterListRequest request, ServerCallContext context)
    {
        var result = await service.GetAll(new CashRegisterQueryDTO
        {
            RestaurantId = request.HasRestaurantId ? ParseId(request.RestaurantId, "restaurant") : null,
            Search = request.HasSearch ? request.Search : null,
            Page = request.Page,
            PageSize = request.PageSize
        }, context.CancellationToken);
        var reply = new CashRegisterPage
        {
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize
        };
        reply.Items.AddRange(result.Items.Select(Map));
        return reply;
    }

    public override async Task<CashRegisterInfo> GetById(CashRegisterIdRequest request, ServerCallContext context)
    {
        return Map(await service.GetById(ParseId(request.CashRegisterId, "cash register"), context.CancellationToken));
    }

    private static Guid ParseId(string value, string resource)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new BillingException(400, $"A valid {resource} ID is required.");
        }

        return id;
    }

    private static CashRegisterInfo Map(CashRegisterDTO value)
    {
        return new CashRegisterInfo
        {
            CashRegisterId = value.CashRegisterId.ToString(),
            RestaurantId = value.RestaurantId.ToString(),
            Code = value.Code
        };
    }
}
