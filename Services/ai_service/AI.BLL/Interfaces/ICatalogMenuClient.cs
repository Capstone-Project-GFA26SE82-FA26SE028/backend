using AI.Common.DTOs;

namespace AI.BLL.Interfaces;

public interface ICatalogMenuClient
{
    Task<MenuPageDTO> GetAllAsync(GuestContext guest, string channel, int page, int pageSize, CancellationToken ct);

    Task<MenuItemDTO?> GetByIdAsync(GuestContext guest, string channel, Guid itemId, CancellationToken ct);
}
