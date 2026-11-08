using Restaurant.Common.DTOs;
using Restaurant.DAL.Models;

namespace Restaurant.DAL.Interfaces;

public interface IReservationRepository
{
    Task AddAsync(Reservation reservation, CancellationToken ct);

    Task<PageResult<Reservation>> GetPageAsync(ReservationSearch query, CancellationToken ct);

    Task<Reservation?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<Reservation>> GetScheduledForTablesAsync(IReadOnlyCollection<Guid> tableIds, Guid? exceptReservationId, CancellationToken ct);

    Task<IReadOnlyList<Guid>> GetNoShowCandidatesAsync(DateTime now, int batchSize, CancellationToken ct);

    Task<IReadOnlyList<Guid>> GetExpirationCandidatesAsync(DateTime now, int batchSize, CancellationToken ct);

    Task<Reservation?> LockAsync(Guid id, CancellationToken ct);

    DateTime NormalizeTimestamp(DateTime utc);
}
