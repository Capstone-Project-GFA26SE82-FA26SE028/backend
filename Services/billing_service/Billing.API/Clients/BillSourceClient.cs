using System.Globalization;
using Backend.Contracts.Ordering;
using Billing.API.Settings;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace Billing.API.Clients;

public sealed class BillSourceClient(BillingOrderGrpc.BillingOrderGrpcClient client,
    IHttpContextAccessor http, IOptions<RestaurantAccessSettings> settings) : IBillSourceClient
{
    public async Task<BillSourceDTO> GetBySessionAsync(Guid sessionId, CancellationToken ct)
    {
        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            throw new BillingException(401, "Yêu cầu phiên làm việc của nhân viên hợp lệ.");
        }

        BillingOrderSnapshot reply;
        try
        {
            reply = await client.GetBySessionAsync(new BillingOrderRequest
            {
                TableSessionId = sessionId.ToString()
            }, new Metadata
            {
                { "authorization", authorization }
            }, deadline: DateTime.UtcNow.AddSeconds(settings.Value.TimeoutSeconds), cancellationToken: ct);
        }
        catch (RpcException error) when (error.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.Internal)
        {
            throw new BillingException(503, "Hệ thống hiện đang bận hoặc tạm thời không khả dụng, vui lòng thử lại sau.");
        }
        catch (RpcException error) when (error.StatusCode is StatusCode.AlreadyExists or StatusCode.FailedPrecondition or StatusCode.NotFound)
        {
            throw new BillingException(error.StatusCode == StatusCode.NotFound ? 404 : 409,
                "Dữ liệu phiên bàn hoặc đơn hàng nguồn không còn khả dụng để xác minh thanh toán.");
        }

        try
        {
            var result = new BillSourceDTO
            {
                TableSessionId = Guid.Parse(reply.TableSessionId),
                RestaurantId = Guid.Parse(reply.RestaurantId),
                PendingOrderCount = reply.PendingOrderCount,
                Items = reply.Items.Select(line => new BillSourceLineDTO
                {
                    OrderItemId = Guid.Parse(line.OrderItemId),
                    OriginalTableId = Guid.Parse(line.OriginalTableId),
                    ComboGroupId = line.HasComboGroupId ? Guid.Parse(line.ComboGroupId) : null,
                    Quantity = line.Quantity,
                    Item = new BillItemSnapshot
                    {
                        OrderId = Guid.Parse(line.OrderId),
                        ItemId = Guid.Parse(line.ItemId),
                        Name = line.Name,
                        Status = line.Status,
                        BaseUnitPrice = decimal.Parse(line.UnitPrice, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture),
                        Options = line.Options.Select(option => new BillOptionSnapshot
                        {
                            OptionId = Guid.Parse(option.OptionId),
                            Name = option.Name,
                            Quantity = option.Quantity,
                            ExtraPrice = decimal.Parse(option.ExtraPrice, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)
                        }).ToList()
                    }
                }).ToList()
            };
            if (result.TableSessionId != sessionId || result.RestaurantId == Guid.Empty || result.PendingOrderCount < 0)
            {
                throw new BillingException(503, "Dịch vụ gọi món trả về bản chụp thanh toán không hợp lệ.");
            }

            return result;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            throw new BillingException(503, "Dịch vụ gọi món trả về dữ liệu thanh toán sai định dạng.");
        }
    }
}
