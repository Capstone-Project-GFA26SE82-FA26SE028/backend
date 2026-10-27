using System.Globalization;
using Backend.Contracts.Ordering;
using Grpc.Core;
using Ordering.BLL.Interfaces;

namespace Ordering.API.GrpcServices;

public sealed class BillingOrderGrpcService(IBillingOrderService service) : BillingOrderGrpc.BillingOrderGrpcBase
{
    public override async Task<BillingOrderSnapshot> GetBySession(BillingOrderRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.TableSessionId, out var id) || id == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "A valid table session ID is required."));
        }

        var source = await service.GetBySession(id, context.CancellationToken);
        var reply = new BillingOrderSnapshot
        {
            TableSessionId = source.TableSessionId.ToString(),
            RestaurantId = source.RestaurantId.ToString(),
            PendingOrderCount = source.PendingOrderCount
        };
        reply.Items.AddRange(source.Items.Select(line =>
        {
            var value = new BillingOrderLine
            {
                OrderId = line.OrderId.ToString(),
                OrderItemId = line.OrderItemId.ToString(),
                ItemId = line.ItemId.ToString(),
                OriginalTableId = line.OriginalTableId.ToString(),
                Name = line.Name,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice.ToString(CultureInfo.InvariantCulture),
                Status = line.Status
            };
            if (line.ComboGroupId.HasValue)
            {
                value.ComboGroupId = line.ComboGroupId.Value.ToString();
            }

            value.Options.AddRange(line.Options.Select(option => new QuotedOptionInfo
            {
                OptionId = option.OptionId.ToString(),
                Name = option.Name,
                Quantity = option.Quantity,
                ExtraPrice = option.ExtraPrice.ToString(CultureInfo.InvariantCulture)
            }));
            return value;
        }));
        return reply;
    }
}
