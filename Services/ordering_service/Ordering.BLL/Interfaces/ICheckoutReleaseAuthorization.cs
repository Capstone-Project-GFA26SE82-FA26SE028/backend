namespace Ordering.BLL.Interfaces;

public interface ICheckoutReleaseAuthorization
{
    Task RequireAsync(Guid sessionId, Guid checkoutId, CancellationToken ct);
}
