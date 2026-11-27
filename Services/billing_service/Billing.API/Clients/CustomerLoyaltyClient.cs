using System.Globalization;
using Billing.API.Settings;
using Billing.Common.Exceptions;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Backend.Contracts.Customer;
using Billing.BLL.Interfaces;
using Microsoft.Extensions.Logging;

namespace Billing.API.Clients;

public sealed class CustomerLoyaltyClient(
    CustomerGrpc.CustomerGrpcClient client,
    ILogger<CustomerLoyaltyClient> logger,
    IHttpContextAccessor http, IOptions<RestaurantAccessSettings> settings) : ICustomerLoyaltyClient
{
    public async Task<Guid> ResolveForBillAsync(Guid restaurantId, Guid? customerId, string? phoneNumber, CancellationToken ct)
    {
        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            throw new BillingException(401, "Yêu cầu phiên làm việc của nhân viên hợp lệ.");
        }

        var options = new CallOptions(new Metadata { { "authorization", authorization } },
            DateTime.UtcNow.AddSeconds(settings.Value.TimeoutSeconds), ct);
        try
        {
            var customer = customerId.HasValue
                ? await client.GetByIdAsync(new CustomerIdRequest
                {
                    CustomerId = customerId.Value.ToString(),
                    RestaurantId = restaurantId.ToString()
                }, options)
                : await client.GetOrCreateAsync(new CreateCustomerRequest
                {
                    Phone = phoneNumber!.Trim(),
                    RestaurantId = restaurantId.ToString()
                }, options);
            if (!Guid.TryParse(customer.CustomerId, out var resolvedId) || resolvedId == Guid.Empty)
            {
                throw new BillingException(503, "Dịch vụ khách hàng trả về mã thành viên không hợp lệ.");
            }

            return resolvedId;
        }
        catch (RpcException error) when (!ct.IsCancellationRequested)
        {
            throw error.StatusCode switch
            {
                StatusCode.InvalidArgument => new BillingException(400, error.Status.Detail),
                StatusCode.Unauthenticated => new BillingException(401, error.Status.Detail),
                StatusCode.PermissionDenied => new BillingException(403, error.Status.Detail),
                StatusCode.NotFound => new BillingException(404, "Không tìm thấy khách hàng tại nhà hàng của hóa đơn."),
                StatusCode.AlreadyExists or StatusCode.FailedPrecondition => new BillingException(409, error.Status.Detail),
                _ => new BillingException(503, "Không thể tra cứu khách hàng. Vui lòng thử lại.")
            };
        }
    }

    public async Task EarnPointsAsync(Guid customerId, Guid billId, decimal finalAmount, CancellationToken ct)
    {
        try
        {
            await client.EarnPointsAsync(new EarnPointsRequest
            {
                CustomerId = customerId.ToString(),
                BillId = billId.ToString(),
                FinalAmount = finalAmount.ToString("0.##", CultureInfo.InvariantCulture)
            }, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không thể tích điểm thành viên cho khách hàng {CustomerId} tại hóa đơn {BillId}.", customerId, billId);
        }
    }

    public async Task CommitPointsAsync(Guid billId, CancellationToken ct)
    {
        try
        {
            await client.CommitPointsAsync(new BillIdRequest
            {
                BillId = billId.ToString()
            }, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không thể xác nhận điểm thưởng đã giữ cho hóa đơn {BillId}.", billId);
        }
    }
}
