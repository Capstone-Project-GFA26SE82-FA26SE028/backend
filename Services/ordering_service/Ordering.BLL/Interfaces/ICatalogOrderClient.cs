using Ordering.Common.DTOs;

namespace Ordering.BLL.Interfaces;

public interface ICatalogOrderClient
{
    Task<QuoteDTO> ValidateAsync(OrderingActor actor, string guestToken, List<SelectionDTO> items, CancellationToken ct);
}
