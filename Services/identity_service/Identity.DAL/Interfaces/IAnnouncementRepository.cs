using Identity.Domain.Models;

namespace Identity.DAL.Interfaces;

public interface IAnnouncementRepository
{
    Task<(List<Announcement> Items, int Total)> GetAllAsync(Guid restaurantId, int page, int pageSize, CancellationToken ct);
    Task<Announcement?> GetByIdAsync(Guid restaurantId, Guid id, CancellationToken ct);
    void Create(Announcement value);
    void Delete(Announcement value);
}
