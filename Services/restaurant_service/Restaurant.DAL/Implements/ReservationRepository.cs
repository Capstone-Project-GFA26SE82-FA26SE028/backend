using System.Data.SqlTypes;
using Microsoft.EntityFrameworkCore;
using Restaurant.Common.DTOs;
using Restaurant.Common.Reservations;
using Restaurant.DAL.Interfaces;
using Restaurant.DAL.Models;

namespace Restaurant.DAL.Implements;

public sealed class ReservationRepository(RestaurantDbContext db) : IReservationRepository
{
    public async Task AddAsync(Reservation reservation, CancellationToken ct)
    {
        await db.Reservations.AddAsync(reservation, ct);
    }

    public async Task<PageResult<Reservation>> GetPageAsync(ReservationSearch query, CancellationToken ct)
    {
        var source = Read();
        if (query.AllowedRestaurantIds is not null)
        {
            source = source.Where(reservation => query.AllowedRestaurantIds.Contains(reservation.RestaurantId));
        }

        if (query.RestaurantId.HasValue)
        {
            source = source.Where(reservation => reservation.RestaurantId == query.RestaurantId.Value);
        }

        if (query.RequestedAreaId.HasValue)
        {
            source = source.Where(reservation => reservation.RequestedAreaId == query.RequestedAreaId.Value);
        }

        if (query.AssignedRestaurantId.HasValue)
        {
            source = source.Where(reservation => reservation.Tables.Any()
                && reservation.Tables.All(table => table.Area.RestaurantId == query.AssignedRestaurantId.Value));
        }

        if (query.AssignedAreaId.HasValue)
        {
            source = source.Where(reservation => reservation.Tables.Any()
                && reservation.Tables.All(table => table.AreaId == query.AssignedAreaId.Value));
        }

        if (query.UnassignedOnly)
        {
            source = source.Where(reservation => !reservation.Tables.Any());
        }

        if (query.Status is not null)
        {
            source = source.Where(reservation => reservation.Status == query.Status);
        }

        if (query.From.HasValue)
        {
            source = source.Where(reservation => reservation.StartAt >= query.From.Value);
        }

        if (query.To.HasValue)
        {
            source = source.Where(reservation => reservation.StartAt < query.To.Value);
        }

        var count = await source.CountAsync(ct);
        var items = await source.OrderBy(reservation => reservation.StartAt).ThenBy(reservation => reservation.ReservationId)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new PageResult<Reservation>
        {
            Items = items,
            TotalCount = count,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    public Task<Reservation?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return Read().SingleOrDefaultAsync(reservation => reservation.ReservationId == id, ct);
    }

    public async Task<IReadOnlyList<Reservation>> GetScheduledForTablesAsync(
        IReadOnlyCollection<Guid> tableIds, Guid? exceptReservationId, CancellationToken ct)
    {
        return await Read().Where(reservation => (reservation.Status == ReservationStatuses.Confirmed
                || reservation.Status == ReservationStatuses.CheckedIn)
            && (!exceptReservationId.HasValue || reservation.ReservationId != exceptReservationId.Value)
            && reservation.Tables.Any(table => tableIds.Contains(table.TableId))).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> GetNoShowCandidatesAsync(DateTime now, int batchSize, CancellationToken ct)
    {
        return await db.Reservations.AsNoTracking()
            .Where(reservation => reservation.Status == ReservationStatuses.Confirmed
                && reservation.HoldUntil.HasValue && reservation.HoldUntil >= reservation.StartAt
                && reservation.EndAt > reservation.StartAt && reservation.HoldUntil <= now)
            .OrderBy(reservation => reservation.HoldUntil).ThenBy(reservation => reservation.ReservationId)
            .Select(reservation => reservation.ReservationId).Take(batchSize).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> GetExpirationCandidatesAsync(DateTime now, int batchSize, CancellationToken ct)
    {
        return await db.Reservations.AsNoTracking()
            .Where(reservation => reservation.Status == ReservationStatuses.Pending
                && reservation.StartAt < now)
            .OrderBy(reservation => reservation.StartAt).ThenBy(reservation => reservation.ReservationId)
            .Select(reservation => reservation.ReservationId).Take(batchSize).ToListAsync(ct);
    }

    public Task<Reservation?> LockAsync(Guid id, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Cần có giao dịch để khóa yêu cầu đặt bàn.");
        }

        var source = db.Database.IsSqlServer()
            ? db.Reservations.FromSqlInterpolated($"SELECT * FROM [Reservation] WITH (UPDLOCK, HOLDLOCK) WHERE [ReservationId] = {id}")
            : db.Reservations.Where(reservation => reservation.ReservationId == id);
        return source.Include(reservation => reservation.TableSession)
            .Include(reservation => reservation.Tables).ThenInclude(table => table.Area).SingleOrDefaultAsync(ct);
    }

    public DateTime NormalizeTimestamp(DateTime utc)
    {
        try
        {
            return DateTime.SpecifyKind(new SqlDateTime(utc).Value, DateTimeKind.Utc);
        }
        catch (SqlTypeException exception)
        {
            throw new ArgumentOutOfRangeException(nameof(utc), exception.Message);
        }
    }

    private IQueryable<Reservation> Read()
    {
        return db.Reservations.AsNoTracking().Include(reservation => reservation.Tables).ThenInclude(table => table.Area)
            .Include(reservation => reservation.TableSession);
    }
}
