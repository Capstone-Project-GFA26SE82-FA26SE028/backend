using System.Globalization;
using Backend.Contracts.Customer;
using Billing.BLL.Interfaces;
using Microsoft.Extensions.Logging;

namespace Billing.API.Clients;

public sealed class CustomerLoyaltyClient(
    CustomerGrpc.CustomerGrpcClient client,
    ILogger<CustomerLoyaltyClient> logger) : ICustomerLoyaltyClient
{
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
