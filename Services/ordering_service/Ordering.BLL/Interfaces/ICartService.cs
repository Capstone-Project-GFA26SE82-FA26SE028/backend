using Ordering.Common.DTOs;

namespace Ordering.BLL.Interfaces;

public interface ICartService
{
    Task<CartDTO> Create(Guid sessionId, string guestToken, CancellationToken ct);

    Task<CartDTO> GetById(Guid sessionId, Guid id, string guestToken, CancellationToken ct);

    Task<CartDTO> Update(Guid sessionId, Guid id, CartWriteDTO request, string guestToken, CancellationToken ct);

    Task Delete(Guid sessionId, Guid id, string version, string guestToken, CancellationToken ct);
}
