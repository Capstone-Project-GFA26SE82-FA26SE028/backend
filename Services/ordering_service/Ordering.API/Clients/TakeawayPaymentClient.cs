using System.Globalization;
using Backend.Contracts.Billing;
using Grpc.Core;
using Ordering.BLL.Interfaces;
using Ordering.Common.DTOs;
using Ordering.Common.Exceptions;
using Ordering.DAL.Models;

namespace Ordering.API.Clients;

public sealed class TakeawayPaymentClient(OnlinePaymentGrpc.OnlinePaymentGrpcClient client,
    IConfiguration configuration, IHttpContextAccessor http) : ITakeawayPaymentClient
{
    public async Task<TakeawayPaymentResult> ExecuteAsync(Order order, string action,
        string reason, string reference, string evidence, CancellationToken ct)
    {
        var key = configuration["Takeaway:ServiceKey"];
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
        {
            throw new OrderingException(503, "Dịch vụ đặt món và thanh toán chưa được cấu hình khóa xác thực chung.");
        }

        var headers = new Metadata { { "x-takeaway-key", key } };
        var bearer = http.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(bearer))
        {
            headers.Add("authorization", bearer);
        }

        var amount = order.OrderItems.Sum(item => item.Quantity * (item.UnitPriceSnapshot
            + item.OrderItemOptions.Sum(option => option.Quantity * option.ExtraPriceSnapshot)));
        var result = await client.ExecuteAsync(new OnlinePaymentRequest
        {
            OrderId = order.OrderId.ToString(),
            RestaurantId = order.RestaurantRef!.Value.ToString(),
            CustomerUserId = order.CustomerUserRef!.Value.ToString(),
            Amount = amount.ToString(CultureInfo.InvariantCulture),
            Action = action,
            Reason = reason,
            Reference = reference,
            Evidence = evidence
        }, headers, deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: ct);
        return new TakeawayPaymentResult(result.Status, string.IsNullOrEmpty(result.CheckoutUrl) ? null : result.CheckoutUrl,
            DateTimeOffset.TryParse(result.PaidAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var paid) ? paid : null,
            DateTimeOffset.TryParse(result.RefundedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var refunded) ? refunded : null,
            string.IsNullOrEmpty(result.RefundReference) ? null : result.RefundReference);
    }
}
