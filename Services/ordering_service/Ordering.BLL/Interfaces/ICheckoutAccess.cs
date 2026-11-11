namespace Ordering.BLL.Interfaces;

public interface ICheckoutAccess
{
    Task RequireAsync(Guid sessionId, string guestToken, bool write, CancellationToken ct);
    Task RequireRequesterAsync(Guid sessionId, string guestToken, CancellationToken ct);
}
