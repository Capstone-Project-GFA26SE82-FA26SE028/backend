using AI.Common.DTOs;

namespace AI.BLL.Interfaces;

public interface ICartClient
{
    Task<CartContextDTO> GetByIdAsync(GuestContext guest, Guid cartId, CancellationToken ct);
}
