using AI.Common.DTOs;

namespace AI.BLL.Interfaces;

public interface ICatalogMenuClient
{
    Task<MenuPageDTO> GetAllAsync(GuestContext guest, Guid restaurantId, string channel, int page, int pageSize, CancellationToken ct);

    Task<MenuItemDTO?> GetByIdAsync(GuestContext guest, Guid restaurantId, string channel, Guid itemId, CancellationToken ct);
}
