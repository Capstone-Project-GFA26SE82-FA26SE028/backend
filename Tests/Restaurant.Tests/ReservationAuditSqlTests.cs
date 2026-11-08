using System.Net;
using ApiGateway.DTOs.Restaurant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Restaurant.DAL.Models;
using Xunit;

namespace Restaurant.Tests;

public sealed partial class ReservationSqlServerTests
{
    [SqlServerFact]
    public async Task ConfirmationAndCancellationAuditValuesRoundTripAndSurviveRepeatedRequests()
    {
        var location = await Location();
        var pending = await Pending(location);
        Assert.Null(pending.ConfirmedAt);
        Assert.Null(pending.CancelledAt);
        Assert.Null(pending.CancelledByRef);
        Assert.Equal(HttpStatusCode.Forbidden, (await Confirm(pending, location.TableId, "admin")).StatusCode);
        Assert.Null((await ReadReservation(pending.ReservationId)).ConfirmedAt);

        var confirmed = await Read<ReservationDto>(await Confirm(pending, location.TableId));
        Assert.Equal(_clock.Now, confirmed.ConfirmedAt);
        Assert.Equal(_users["waiter"], confirmed.ApprovedByRef);
        Assert.Null(confirmed.CancelledAt);
        Assert.Null(confirmed.CancelledByRef);
        _clock.Now = _clock.Now.AddMinutes(5);
        Assert.Equal(HttpStatusCode.Conflict, (await Confirm(pending, location.TableId)).StatusCode);
        var moved = await Read<ReservationDto>(await Reschedule(confirmed, confirmed.StartAt.AddHours(1), location.TableId));
        Assert.Equal(confirmed.ConfirmedAt, moved.ConfirmedAt);
        Assert.Equal(HttpStatusCode.Forbidden, (await Action(moved, "cancel", "admin")).StatusCode);
        Assert.Null((await ReadReservation(moved.ReservationId)).CancelledAt);

        var cancelled = await Read<ReservationDto>(await Action(moved, "cancel", "cashier"));
        Assert.Equal(_clock.Now, cancelled.CancelledAt);
        Assert.Equal(_users["cashier"], cancelled.CancelledByRef);
        Assert.Equal(confirmed.ConfirmedAt, cancelled.ConfirmedAt);
        Assert.Equal(confirmed.ApprovedByRef, cancelled.ApprovedByRef);
        _clock.Now = _clock.Now.AddMinutes(5);
        Assert.Equal(HttpStatusCode.Conflict, (await Action(moved, "cancel")).StatusCode);
        var stored = await ReadReservation(moved.ReservationId);
        Assert.Equal(cancelled.CancelledAt, stored.CancelledAt);
        Assert.Equal(cancelled.CancelledByRef, stored.CancelledByRef);
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        var entity = await db.Reservations.AsNoTracking().SingleAsync(value => value.ReservationId == pending.ReservationId);
        Assert.Equal(confirmed.ConfirmedAt!.Value.UtcDateTime, entity.ConfirmedAt);
        Assert.Equal(cancelled.CancelledAt!.Value.UtcDateTime, entity.CancelledAt);
        Assert.Equal(_users["cashier"], entity.CancelledByRef);
    }

    [SqlServerFact]
    public async Task RejectedExpiredAndNoShowDoNotInventCancellationAuditValues()
    {
        var location = await Location();
        var rejected = await Read<ReservationDto>(await Action(await Pending(location), "reject"));
        Assert.Null(rejected.ConfirmedAt);
        var expired = await Pending(location, _clock.Now.AddMinutes(30));
        var confirmed = await Confirmed(location);
        _clock.Now = confirmed.HoldUntil!.Value;
        await RunWorker();
        foreach (var id in new[] { rejected.ReservationId, expired.ReservationId, confirmed.ReservationId })
        {
            var stored = await ReadReservation(id);
            Assert.Null(stored.CancelledAt);
            Assert.Null(stored.CancelledByRef);
        }

        Assert.Null((await ReadReservation(expired.ReservationId)).ConfirmedAt);
        Assert.Equal(confirmed.ConfirmedAt, (await ReadReservation(confirmed.ReservationId)).ConfirmedAt);
    }

    [SqlServerFact]
    public async Task FailedCancellationTransactionDoesNotPersistAuditValues()
    {
        var location = await Location();
        var confirmed = await Confirmed(location);
        _clock.Now = _clock.Now.AddMinutes(1);
        _writeFault.Arm();
        var response = await Action(confirmed, "cancel");
        Assert.False(response.IsSuccessStatusCode);
        var stored = await ReadReservation(confirmed.ReservationId);
        Assert.Equal("Confirmed", stored.Status);
        Assert.Equal(confirmed.ConfirmedAt, stored.ConfirmedAt);
        Assert.Null(stored.CancelledAt);
        Assert.Null(stored.CancelledByRef);
    }
}
