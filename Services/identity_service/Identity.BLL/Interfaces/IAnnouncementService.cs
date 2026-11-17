using Shared.Common.Notifications;
using Shared.Common.Pagination;

namespace Identity.BLL.Interfaces;

public interface IAnnouncementService
{
    Task<AnnouncementItem> Create(Guid userId, Guid restaurantId, AnnouncementWrite request, CancellationToken ct);
    Task<PagedResult<AnnouncementItem>> GetAll(Guid userId, Guid restaurantId, int page, int pageSize, CancellationToken ct);
    Task<AnnouncementItem> GetById(Guid userId, Guid restaurantId, Guid id, CancellationToken ct);
    Task<AnnouncementItem> Update(Guid userId, Guid restaurantId, Guid id, AnnouncementWrite request, CancellationToken ct);
    Task Delete(Guid userId, Guid restaurantId, Guid id, Guid version, CancellationToken ct);
    Task<AnnouncementItem> Send(Guid userId, Guid restaurantId, Guid id, Guid version, CancellationToken ct);
}
