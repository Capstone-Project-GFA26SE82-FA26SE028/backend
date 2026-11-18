using Restaurant.Common.DTOs;

namespace Restaurant.BLL.Interfaces;

public interface IPaymentRequestOrderClient
{
    Task<PaymentRequestReviewDto> GetReviewAsync(Guid sessionId, Guid restaurantId, CancellationToken ct);
}
