using System.Net;
using System.Data.Common;
using ApiGateway.DTOs.Restaurant;
using Identity.Domain.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.Settings;
using Restaurant.DAL.Models;
using Xunit;

namespace Restaurant.Tests;

public sealed partial class ReservationSqlServerTests
{
    [SqlServerFact]
    public async Task CheckInUsesTheChosenWindowAndOnlyRecordsArrival()
    {
        var location = await Location();
        var reservation = await Confirmed(location);
        await SeedServingData(location);
        var servingBefore = await ServingSnapshot();
        Assert.Equal(HttpStatusCode.Conflict, (await Action(reservation, "check-in")).StatusCode);
        _clock.Now = reservation.StartAt.AddMinutes(-30);
        var checkedIn = await Read<ReservationDto>(await Action(reservation, "check-in"));
        Assert.Equal("CheckedIn", checkedIn.Status);
        Assert.Equal(reservation.ApprovedByRef, checkedIn.ApprovedByRef);
        Assert.Equal(reservation.EndAt, checkedIn.EndAt);
        Assert.Null(checkedIn.TableSessionId);
        Assert.Equal(servingBefore, await ServingSnapshot());
        Assert.Equal(HttpStatusCode.Conflict, (await Action(reservation, "check-in")).StatusCode);
        _clock.Now = reservation.HoldUntil!.Value.AddHours(1);
        await RunWorker();
        Assert.Equal("CheckedIn", (await ReadReservation(reservation.ReservationId)).Status);
        Assert.Equal(servingBefore, await ServingSnapshot());
    }

    [SqlServerFact]
    public async Task CheckInAtTheArrivalDeadlineCommitsNoShowAndReleasesTheSchedule()
    {
        var location = await Location();
        var reservation = await Confirmed(location);
        _clock.Now = reservation.HoldUntil!.Value;
        Assert.Equal(HttpStatusCode.Conflict, (await Action(reservation, "check-in")).StatusCode);
        var noShow = await ReadReservation(reservation.ReservationId);
        Assert.Equal("NoShow", noShow.Status);
        Assert.Equal(reservation.AssignedTableIds, noShow.AssignedTableIds);
        Assert.Equal(reservation.ApprovedByRef, noShow.ApprovedByRef);
        var next = await Pending(location, _clock.Now.AddMinutes(30));
        Assert.Equal(HttpStatusCode.OK, (await Confirm(next, location.TableId)).StatusCode);
    }

    [SqlServerFact]
    public async Task WorkerSeparatesPendingExpiryFromNoShowWithoutUsingSessionPresence()
    {
        var first = await Location();
        var second = await Location();
        var confirmed = await Confirmed(first);
        var pending = await Pending(second);
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            db.TableSessions.Add(new TableSession
            {
                TableSessionId = Guid.NewGuid(),
                ReservationId = confirmed.ReservationId,
                Status = "Open",
                PartySize = 4,
                OpenedAt = _clock.Now.UtcDateTime
            });
            await db.SaveChangesAsync();
        }

        _clock.Now = confirmed.HoldUntil!.Value;
        var snapshot = await ServingSnapshot();
        Assert.Equal(2, await RunWorker());
        Assert.Equal(0, await RunWorker());
        Assert.Equal("NoShow", (await ReadReservation(confirmed.ReservationId, "admin")).Status);
        Assert.Equal("Expired", (await ReadReservation(pending.ReservationId, "admin")).Status);
        Assert.Equal(snapshot, await ServingSnapshot());
    }

    [SqlServerFact]
    public async Task CancelPreservesAssignmentsAndApproverAndReleasesTheSchedule()
    {
        var location = await Location();
        var pending = await Pending(location);
        Assert.Equal(HttpStatusCode.Conflict, (await Action(pending, "cancel")).StatusCode);
        var confirmed = await Read<ReservationDto>(await Confirm(pending, location.TableId));
        _clock.Now = confirmed.StartAt.AddMinutes(-30);
        var cancelled = await Read<ReservationDto>(await Action(confirmed, "cancel"));
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal(confirmed.AssignedTableIds, cancelled.AssignedTableIds);
        Assert.Equal(confirmed.ApprovedByRef, cancelled.ApprovedByRef);
        Assert.Equal(confirmed.StartAt, cancelled.StartAt);
        Assert.Equal(confirmed.EndAt, cancelled.EndAt);
        Assert.Equal(HttpStatusCode.Conflict, (await Action(confirmed, "cancel")).StatusCode);
        var replacement = await Pending(location, confirmed.StartAt);
        Assert.Equal(HttpStatusCode.OK, (await Confirm(replacement, location.TableId)).StatusCode);
        Assert.Equal("Cancelled", (await ReadReservation(confirmed.ReservationId)).Status);
    }

    [SqlServerFact]
    public async Task UpdateIsAtomicExcludesItselfAndUsesConfiguredDuration()
    {
        var location = await Location();
        var reservation = await Confirmed(location);
        var same = await Read<ReservationDto>(await Reschedule(reservation, reservation.StartAt, location.TableId));
        Assert.Equal(reservation.ReservationId, same.ReservationId);
        Assert.Equal("Confirmed", same.Status);
        var secondTable = await ExtraTable(location, "T2");
        using (var scope = _restaurant.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IOptions<ReservationSettings>>().Value.DefaultDurationMinutes = 90;
        }

        Assert.Equal(reservation.EndAt, (await ReadReservation(reservation.ReservationId)).EndAt);
        var updated = await Read<ReservationDto>(await Reschedule(reservation, reservation.StartAt.AddHours(4), secondTable));
        Assert.Equal(reservation.ReservationId, updated.ReservationId);
        Assert.Equal("Confirmed", updated.Status);
        Assert.Equal(reservation.ApprovedByRef, updated.ApprovedByRef);
        Assert.Equal(updated.StartAt.AddMinutes(90), updated.EndAt);
        Assert.Equal(updated.StartAt.AddMinutes(30), updated.HoldUntil);
        Assert.Equal(secondTable, Assert.Single(updated.AssignedTableIds));
        var conflicting = await Pending(location, updated.StartAt.AddHours(3));
        await Read<ReservationDto>(await Confirm(conflicting, secondTable));
        Assert.Equal(HttpStatusCode.Conflict,
            (await Reschedule(updated, conflicting.StartAt, secondTable)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Reschedule(updated, new DateTimeOffset(2030, 1, 7, 23, 0, 0, TimeSpan.Zero), secondTable)).StatusCode);
        var afterFailure = await ReadReservation(reservation.ReservationId);
        Assert.Equal(updated.StartAt, afterFailure.StartAt);
        Assert.Equal(updated.EndAt, afterFailure.EndAt);
        Assert.Equal(updated.HoldUntil, afterFailure.HoldUntil);
        Assert.Equal(updated.AssignedTableIds, afterFailure.AssignedTableIds);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(HttpMethod.Put,
            $"/api/reservations/{reservation.ReservationId}", "waiter", new
            {
                startAt = updated.StartAt.ToString("O"),
                tableIds = new[] { secondTable },
                status = "Pending",
                endAt = updated.EndAt.ToString("O")
            })).StatusCode);
    }

    [SqlServerFact]
    public async Task UpdateWriteFailureRollsBackTheScheduleAndTableLinks()
    {
        var location = await Location();
        var reservation = await Confirmed(location);
        var target = await ExtraTable(location, "T2");
        _writeFault.Arm();
        Assert.Equal(HttpStatusCode.BadGateway,
            (await Reschedule(reservation, reservation.StartAt.AddHours(4), target)).StatusCode);
        var stored = await ReadReservation(reservation.ReservationId);
        Assert.True(_writeFault.Triggered);
        Assert.Equal(reservation.StartAt, stored.StartAt);
        Assert.Equal(reservation.EndAt, stored.EndAt);
        Assert.Equal(reservation.HoldUntil, stored.HoldUntil);
        Assert.Equal(reservation.ApprovedByRef, stored.ApprovedByRef);
        Assert.Equal(reservation.AssignedTableIds, stored.AssignedTableIds);
        Assert.Equal("Confirmed", stored.Status);
        Assert.Equal(HttpStatusCode.OK,
            (await Reschedule(reservation, reservation.StartAt.AddHours(4), target)).StatusCode);
    }

    [SqlServerFact]
    public async Task CancelAndUpdateRespectTheCurrentDeadlineIncludingItsExactBoundary()
    {
        var location = await Location();
        var reservation = await Confirmed(location);
        _clock.Now = reservation.StartAt.AddMinutes(-30);
        var updated = await Read<ReservationDto>(await Reschedule(reservation, reservation.StartAt.AddHours(3), location.TableId));
        _clock.Now = updated.StartAt.AddMinutes(-30).AddMilliseconds(10);
        Assert.Equal(HttpStatusCode.Conflict, (await Action(updated, "cancel")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Reschedule(updated, updated.StartAt.AddHours(3), location.TableId)).StatusCode);
        Assert.Equal(updated.StartAt, (await ReadReservation(updated.ReservationId)).StartAt);
    }

    [SqlServerFact]
    public async Task CheckedInStillBlocksItsPlannedInterval()
    {
        var location = await Location();
        var reservation = await Confirmed(location);
        _clock.Now = reservation.StartAt.AddMinutes(-30);
        await Read<ReservationDto>(await Action(reservation, "check-in"));
        var next = await Pending(location, reservation.StartAt.AddMinutes(30));
        Assert.Equal(HttpStatusCode.Conflict, (await Confirm(next, location.TableId)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Action(reservation, "cancel")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Reschedule(reservation, reservation.StartAt.AddHours(3), location.TableId)).StatusCode);
    }

    [SqlServerFact]
    public async Task AllNewMutationsEnforceRoleAssignmentAndSession()
    {
        var location = await Location();
        var other = await Location();
        var reservation = await Confirmed(location);
        var outside = await Read<ReservationDto>(await Confirm(await Pending(other), other.TableId, "cashier"));
        foreach (var role in new[] { "admin", "kitchen" })
        {
            await AssertMutationsDenied(reservation, location.TableId, role, HttpStatusCode.Forbidden);
        }

        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Delete,
            $"/api/restaurants/{other.RestaurantId}/staff/{_users["waiter"]}", "admin")).StatusCode);
        await AssertMutationsDenied(outside, other.TableId, "waiter", HttpStatusCode.Forbidden);
        using (var scope = _identity.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            foreach (var session in await db.UserSessions.Where(value => value.UserId == _users["waiter"]).ToListAsync())
            {
                session.RevokedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync();
        }

        await AssertMutationsDenied(reservation, location.TableId, "waiter", HttpStatusCode.Unauthorized);
    }

    [SqlServerFact]
    public async Task ConcurrentUpdatesCannotMoveTwoReservationsOntoOneTable()
    {
        var location = await Location();
        var secondTable = await ExtraTable(location, "T2");
        var targetTable = await ExtraTable(location, "T3");
        var first = await Confirmed(location);
        var second = await Read<ReservationDto>(await Confirm(await Pending(location), secondTable));
        var target = first.StartAt.AddHours(4);
        var responses = await Contend(targetTable,
            () => Reschedule(first, target, targetTable), () => Reschedule(second, target, targetTable));
        Assert.Single(responses, value => value.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, value => value.StatusCode == HttpStatusCode.Conflict);
        var stored = new[] { await ReadReservation(first.ReservationId), await ReadReservation(second.ReservationId) };
        Assert.Single(stored, value => value.StartAt == target && value.AssignedTableIds.SequenceEqual(new[] { targetTable }));
        Assert.Single(stored, value => value.StartAt == first.StartAt && !value.AssignedTableIds.Contains(targetTable));
        Assert.All(stored, value => Assert.Equal("Confirmed", value.Status));
    }

    [SqlServerFact]
    public async Task UpdateAndCancelSerializeWithoutOverwritingCancellation()
    {
        var location = await Location();
        var reservation = await Confirmed(location);
        await using var blocker = new SqlConnection(_restaurantConnection);
        await blocker.OpenAsync();
        await using var transaction = (SqlTransaction)await blocker.BeginTransactionAsync();
        await LockReservation(blocker, transaction, reservation.ReservationId);
        var update = Reschedule(reservation, reservation.StartAt.AddHours(4), location.TableId);
        var cancel = Action(reservation, "cancel");
        var observed = await WaitForBlocked(blocker.ServerProcessId, 2);
        await transaction.CommitAsync();
        Assert.Equal(HttpStatusCode.OK, (await cancel).StatusCode);
        Assert.Contains((await update).StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        Assert.True(observed);
        Assert.Equal("Cancelled", (await ReadReservation(reservation.ReservationId)).Status);
    }

    [SqlServerFact]
    public async Task NoShowWorkerRechecksAReservationThatWasRescheduledAfterCandidateSelection()
    {
        var location = await Location();
        var reservation = await Confirmed(location);
        var newStart = reservation.StartAt.AddHours(4);
        _writeBarrier.Arm(value => value.ReservationId == reservation.ReservationId && value.StartAt == newStart.UtcDateTime);
        var update = Reschedule(reservation, newStart, location.TableId);
        await _writeBarrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(8));
        _clock.Now = reservation.HoldUntil!.Value;
        var worker = RunWorker();
        var observed = await WaitForBlocked(_writeBarrier.SessionId, 1);
        _writeBarrier.Release.TrySetResult();
        Assert.Equal(HttpStatusCode.OK, (await update).StatusCode);
        Assert.Equal(0, await worker);
        Assert.True(observed);
        var current = await ReadReservation(reservation.ReservationId);
        Assert.Equal("Confirmed", current.Status);
        Assert.Equal(newStart, current.StartAt);
        Assert.Equal(newStart.AddMinutes(30), current.HoldUntil);
    }

    [SqlServerFact]
    public async Task CheckInAndNoShowWorkerSerializeAndCheckedInWinsOnceArrivalWasRecorded()
    {
        var location = await Location();
        var reservation = await Confirmed(location);
        _clock.Now = reservation.StartAt.AddMinutes(-30);
        _writeBarrier.Arm(value => value.ReservationId == reservation.ReservationId && value.Status == "CheckedIn");
        var checkIn = Action(reservation, "check-in");
        await _writeBarrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(8));
        _clock.Now = reservation.HoldUntil!.Value;
        var worker = RunWorker();
        var observed = await WaitForBlocked(_writeBarrier.SessionId, 1);
        _writeBarrier.Release.TrySetResult();
        Assert.Equal(HttpStatusCode.OK, (await checkIn).StatusCode);
        Assert.Equal(0, await worker);
        Assert.True(observed);
        Assert.Equal("CheckedIn", (await ReadReservation(reservation.ReservationId)).Status);
    }

    private async Task AssertMutationsDenied(ReservationDto reservation, Guid tableId, string role, HttpStatusCode expected)
    {
        Assert.Equal(expected, (await Action(reservation, "check-in", role)).StatusCode);
        Assert.Equal(expected, (await Action(reservation, "cancel", role)).StatusCode);
        Assert.Equal(expected, (await Reschedule(reservation, reservation.StartAt.AddHours(4), tableId, role)).StatusCode);
    }

    private async Task<ReservationDto> Confirmed(LocationIds location)
    {
        return await Read<ReservationDto>(await Confirm(await Pending(location), location.TableId));
    }

    private Task<HttpResponseMessage> Action(ReservationDto reservation, string operation, string role = "waiter")
    {
        return Send(HttpMethod.Post, $"/api/reservations/{reservation.ReservationId}/{operation}", role);
    }

    private Task<HttpResponseMessage> Reschedule(ReservationDto reservation, DateTimeOffset start, Guid tableId, string role = "waiter")
    {
        return Send(HttpMethod.Put, $"/api/reservations/{reservation.ReservationId}", role, new
        {
            startAt = start.ToString("O"),
            tableIds = new[] { tableId }
        });
    }

    private async Task<ReservationDto> ReadReservation(Guid id, string role = "waiter")
    {
        return await Read<ReservationDto>(await Send(HttpMethod.Get, $"/api/reservations/{id}", role));
    }

    private async Task<Guid> ExtraTable(LocationIds location, string code)
    {
        return (await Read<TableDto>(await Send(HttpMethod.Post, "/api/tables", "admin", new
        {
            areaId = location.AreaId,
            tableCode = code,
            capacity = 4
        }), HttpStatusCode.Created)).TableId;
    }

    private async Task<int> RunWorker()
    {
        using var scope = _restaurant.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IReservationExpirationService>().Expire(CancellationToken.None);
    }

    private static async Task LockReservation(SqlConnection connection, SqlTransaction transaction, Guid id)
    {
        await using var command = new SqlCommand("SELECT ReservationId FROM Reservation WITH (UPDLOCK, HOLDLOCK) WHERE ReservationId = @id", connection, transaction);
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteScalarAsync();
    }

    private async Task SeedServingData(LocationIds location)
    {
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        db.Qrs.Add(new Qr
        {
            QrId = Guid.NewGuid(),
            TableId = location.TableId,
            TokenHash = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
            CreatedAt = _clock.Now.UtcDateTime
        });
        var session = new TableSession
        {
            TableSessionId = Guid.NewGuid(),
            Status = "Open",
            PartySize = 2,
            OpenedAt = _clock.Now.UtcDateTime
        };
        db.SessionTables.Add(new SessionTable
        {
            SessionTableId = Guid.NewGuid(),
            TableId = location.TableId,
            AssignedAt = _clock.Now.UtcDateTime,
            TableSession = session
        });
        db.GuestAccesses.Add(new GuestAccess
        {
            GuestAccessId = Guid.NewGuid(),
            TableSession = session,
            TokenHash = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
            CreatedAt = _clock.Now.UtcDateTime,
            ExpiredAt = _clock.Now.AddHours(4).UtcDateTime
        });
        await db.SaveChangesAsync();
    }

    private async Task<string> ServingSnapshot()
    {
        await using var connection = new SqlConnection(_restaurantConnection);
        await connection.OpenAsync();
        var result = new System.Text.StringBuilder();
        foreach (var table in new[] { "Qr", "GuestAccess", "TableSession", "SessionTable" })
        {
            await using var command = new SqlCommand($"SELECT * FROM [{table}] ORDER BY [{table}Id] FOR JSON PATH, INCLUDE_NULL_VALUES", connection);
            result.Append(await command.ExecuteScalarAsync());
        }

        return result.ToString();
    }

    private sealed class ReservationWriteBarrier : SaveChangesInterceptor
    {
        private Func<Reservation, bool>? _predicate;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int SessionId { get; private set; }

        public void Arm(Func<Reservation, bool> predicate)
        {
            _predicate = predicate;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var context = eventData.Context;
            var predicate = _predicate;
            if (context is not null && predicate is not null
                && context.ChangeTracker.Entries<Reservation>().Any(entry => predicate(entry.Entity)))
            {
                _predicate = null;
                SessionId = ((SqlConnection)context.Database.GetDbConnection()).ServerProcessId;
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(12), cancellationToken);
            }

            return result;
        }
    }

    private sealed class ReservationWriteFault : DbCommandInterceptor
    {
        private int _armed;
        public bool Triggered { get; private set; }

        public void Arm()
        {
            Interlocked.Exchange(ref _armed, 1);
        }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE [Reservation]", StringComparison.Ordinal)
                && Interlocked.Exchange(ref _armed, 0) == 1)
            {
                Triggered = true;
                await result.DisposeAsync();
                throw new InvalidOperationException("Injected test failure after reservation SQL execution.");
            }

            return result;
        }
    }
}
