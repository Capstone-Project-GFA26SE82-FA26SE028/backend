using System.Globalization;
using Backend.Contracts.Billing;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Grpc.Core;

namespace Billing.API.GrpcServices;

public sealed class CashShiftGrpcService(ICashShiftService service) : CashShiftGrpc.CashShiftGrpcBase
{
    public override async Task<CashShiftInfo> Open(OpenCashShiftRequest request, ServerCallContext context)
    {
        var registerId = ParseRegisterId(request.CashRegisterId);
        if (!request.HasOpeningCash || !decimal.TryParse(request.OpeningCash,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
        {
            throw new BillingException(400, "Provide an opening cash amount as an invariant decimal number.");
        }

        return Map(await service.Open(registerId, new OpenCashShiftDTO
        {
            OpeningCash = amount
        }, context.CancellationToken));
    }

    public override async Task<CashShiftInfo> GetCurrent(CashShiftRegisterRequest request, ServerCallContext context)
    {
        return Map(await service.GetCurrent(ParseRegisterId(request.CashRegisterId), context.CancellationToken));
    }

    private static Guid ParseRegisterId(string value)
    {
        if (!Guid.TryParse(value, out var id) || id == Guid.Empty)
        {
            throw new BillingException(400, "A valid cash register ID is required.");
        }

        return id;
    }

    private static CashShiftInfo Map(CashShiftDTO value)
    {
        return new CashShiftInfo
        {
            CashShiftId = value.CashShiftId.ToString(),
            CashRegisterId = value.CashRegisterId.ToString(),
            RestaurantId = value.RestaurantId.ToString(),
            CashierUserRef = value.CashierUserRef.ToString(),
            OpeningCash = value.OpeningCash.ToString(CultureInfo.InvariantCulture),
            OpenedAt = value.OpenedAt.ToString("O", CultureInfo.InvariantCulture),
            Status = value.Status
        };
    }
}
