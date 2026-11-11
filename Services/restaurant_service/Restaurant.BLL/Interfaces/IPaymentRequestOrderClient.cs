namespace Restaurant.BLL.Interfaces;

public interface IPaymentRequestOrderClient
{
    Task RequireFrozenAsync(Guid sessionId, Guid checkoutId, CancellationToken ct);
}
