using Ordering.Common.DTOs;

namespace Ordering.BLL.Interfaces;

public interface IOrderingAccess
{
    Task<OrderingActor> RequireAsync(Guid sessionId, Guid? tableId, string guestToken, CancellationToken ct);
}
