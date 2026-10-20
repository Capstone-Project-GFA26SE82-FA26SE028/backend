using Microsoft.Extensions.Options;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.Reservations;
using Restaurant.Common.Settings;
using Restaurant.DAL.Interfaces;

namespace Restaurant.BLL.Implements;

public sealed class ReservationExpirationService(
    IReservationRepository reservations,
    IReservationTransaction transaction,
    IOptions<ReservationSettings> settings,
    TimeProvider clock) : IReservationExpirationService
{
    public async Task<int> Expire(CancellationToken ct)
    {
        ReservationRules.Configuration(settings.Value);
        var cutoff = clock.GetUtcNow().UtcDateTime.AddMinutes(settings.Value.MinimumAdvanceMinutes);
        var ids = await reservations.GetExpirationCandidatesAsync(cutoff, settings.Value.ExpirationBatchSize, ct);
        var expired = 0;
        foreach (var id in ids)
        {
            var changed = await transaction.ExecuteAsync(async () =>
            {
                var reservation = await reservations.LockAsync(id, ct);
                if (reservation is null || reservation.Status != ReservationStatuses.Pending
                    || clock.GetUtcNow().UtcDateTime <= reservation.StartAt.AddMinutes(-settings.Value.MinimumAdvanceMinutes))
                {
                    return false;
                }

                reservation.Status = ReservationStatuses.Expired;
                return true;
            }, ct);
            if (changed)
            {
                expired++;
            }
        }

        var noShowIds = await reservations.GetNoShowCandidatesAsync(clock.GetUtcNow().UtcDateTime,
            settings.Value.ExpirationBatchSize, ct);
        foreach (var id in noShowIds)
        {
            var changed = await transaction.ExecuteAsync(async () =>
            {
                var reservation = await reservations.LockAsync(id, ct);
                if (reservation is null || reservation.Status != ReservationStatuses.Confirmed
                    || !reservation.HoldUntil.HasValue || reservation.HoldUntil.Value < reservation.StartAt
                    || reservation.EndAt <= reservation.StartAt || clock.GetUtcNow().UtcDateTime < reservation.HoldUntil.Value)
                {
                    return false;
                }

                reservation.Status = ReservationStatuses.NoShow;
                return true;
            }, ct);
            if (changed)
            {
                expired++;
            }
        }

        return expired;
    }
}
