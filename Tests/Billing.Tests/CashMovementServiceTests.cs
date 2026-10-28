using Billing.BLL.Implements;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.Common.Settings;
using Billing.DAL.Implements;
using Billing.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Persistence.UnitOfWorks;

namespace Billing.Tests;

public sealed class CashMovementServiceTests
{
    [Fact]
    public async Task ManualCashAndPaymentSaleAreCountedOnceWithDistinctPerformerSources()
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        var service = Make(f.Db, f.Core.Actor);
        var added = await service.Create(f.ShiftId, Guid.NewGuid().ToString(), Body("CashIn", 200), default);
        Assert.True(added.Created);
        Assert.Equal("ShiftOwnerDerived", added.Movement.PerformerSource);
        Assert.Equal(f.Core.Actor.Scope.UserId, added.Movement.PerformedByRef);
        Assert.Null(added.Movement.PaymentId);
        await service.Create(f.ShiftId, Guid.NewGuid().ToString(), Body("CashOut", 300), default);
        var paid = await f.Pay(300);
        var ledger = await service.GetAll(f.ShiftId, 1, 20, default);
        Assert.Equal(1132, ledger.ExpectedCash);
        Assert.Equal(132, ledger.MovementTotal);
        Assert.Equal(3, ledger.Movements.TotalCount);
        var sale = Assert.Single(ledger.Movements.Items, value => value.PaymentId.HasValue);
        Assert.Equal(paid.Payment.PaymentId, sale.PaymentId);
        Assert.Equal("PaymentConfirmation", sale.PerformerSource);
        Assert.Equal(f.Core.Actor.Scope.UserId, sale.PerformedByRef);
        Assert.Equal(-300, Assert.Single(ledger.Movements.Items, value => value.Type == "CashOut").SignedAmount);
        Assert.Equal(added.Movement.CashMovementId, (await service.GetById(added.Movement.CashMovementId, default)).CashMovementId);
    }

    [Fact]
    public async Task ReplayDoesNotPostAgainAndChangedPayloadCannotReuseKey()
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        var service = Make(f.Db, f.Core.Actor);
        var key = Guid.NewGuid().ToString();
        var first = await service.Create(f.ShiftId, key, Body("CashOut", 1000), default);
        var shift = await f.Db.CashShifts.SingleAsync();
        shift.Status = "Closed";
        shift.ClosedAt = DateTime.UtcNow;
        await f.Db.SaveChangesAsync();
        var replay = await service.Create(f.ShiftId, key, Body("CashOut", 1000), default);
        Assert.False(replay.Created);
        Assert.Equal(first.Movement.CashMovementId, replay.Movement.CashMovementId);
        await Status(409, () => service.Create(f.ShiftId, key, Body("CashOut", 999), default));
        await Status(409, () => service.Create(f.ShiftId, Guid.NewGuid().ToString(), Body("CashIn", 1), default));
        Assert.Equal(0, (await service.GetAll(f.ShiftId, 1, 20, default)).ExpectedCash);
    }

    [Fact]
    public async Task InsufficientFundsAndMissingReasonDoNotWriteLedgerRows()
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        var service = Make(f.Db, f.Core.Actor);
        await Status(409, () => service.Create(f.ShiftId, Guid.NewGuid().ToString(), Body("CashOut", 1001), default));
        foreach (var amount in new decimal?[] { null, 0, -1, 1.1m, 10000000000000000m })
        {
            await Status(400, () => service.Create(f.ShiftId, Guid.NewGuid().ToString(), Body("CashIn", amount), default));
        }

        foreach (var reason in new[] { "", " ", new string('a', 1001), "Invalid\nreason", "Unstorable \u4e00" })
        {
            var body = Body("CashIn", 1);
            body.Reason = reason;
            await Status(400, () => service.Create(f.ShiftId, Guid.NewGuid().ToString(), body, default));
        }

        await Status(400, () => service.Create(f.ShiftId, "invalid", Body("CashIn", 1), default));
        await Status(400, () => service.Create(f.ShiftId, Guid.NewGuid().ToString(), Body("Sale", 1), default));
        Assert.Empty(await f.Db.CashMovements.ToListAsync());
    }

    [Theory]
    [InlineData("admin", true)]
    [InlineData("manager", false)]
    [InlineData("waiter", false)]
    [InlineData("kitchen", false)]
    public async Task OtherRolesCannotPost(string role, bool administrator)
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        f.Core.Actor.Scope.RoleId = role;
        f.Core.Actor.Scope.IsAdministrator = administrator;
        await Status(403, () => Make(f.Db, f.Core.Actor).Create(f.ShiftId, Guid.NewGuid().ToString(), Body("CashIn", 1), default));
    }

    [Fact]
    public async Task OwnershipAssignmentsAndReadScopesAreEnforced()
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        var service = Make(f.Db, f.Core.Actor);
        var created = await service.Create(f.ShiftId, Guid.NewGuid().ToString(), Body("CashIn", 1), default);
        var owner = f.Core.Actor.Scope.UserId;
        f.Core.Actor.Scope.UserId = Guid.NewGuid();
        await Status(403, () => service.Create(f.ShiftId, Guid.NewGuid().ToString(), Body("CashIn", 1), default));
        f.Core.Actor.Scope.UserId = owner;
        f.Core.Actor.Scope.RoleId = "manager";
        Assert.Equal(1, (await service.GetAll(f.ShiftId, 1, 1, default)).Movements.TotalCount);
        f.Core.Actor.Scope.RestaurantIds = [];
        await Status(403, () => service.GetAll(f.ShiftId, 1, 20, default));
        await Status(403, () => service.GetById(created.Movement.CashMovementId, default));
        f.Core.Actor.Scope.IsAdministrator = true;
        Assert.Equal(1001, (await service.GetAll(f.ShiftId, 1, 20, default)).ExpectedCash);
    }

    [Fact]
    public async Task LegacyMovementsDoNotInventAnActorAndPaginationDoesNotChangeBalance()
    {
        await using var f = await CashPaymentServiceTests.Fixture.Create();
        f.Db.CashMovements.Add(new CashMovement
        {
            CashMovementId = Guid.NewGuid(),
            CashShiftId = f.ShiftId,
            Type = "CashIn",
            SignedAmount = 10,
            PostedAt = DateTime.UtcNow,
            IdempotencyKey = "legacy-key"
        });
        await f.Db.SaveChangesAsync();
        var service = Make(f.Db, f.Core.Actor);
        var result = await service.GetAll(f.ShiftId, 1, 1, default);
        Assert.Equal(1010, result.ExpectedCash);
        Assert.Null(result.Movements.Items[0].PerformedByRef);
        Assert.Equal("Unknown", result.Movements.Items[0].PerformerSource);
        Assert.Empty((await service.GetAll(f.ShiftId, 2, 1, default)).Movements.Items);
        await Status(400, () => service.GetAll(f.ShiftId, 0, 20, default));
        await Status(400, () => service.GetAll(f.ShiftId, int.MaxValue, 100, default));
        await Status(400, () => service.GetById(Guid.Empty, default));
        await Status(404, () => service.GetById(Guid.NewGuid(), default));
    }

    internal static CashMovementService Make(BillingDbContext db, ShiftIdentity actor)
    {
        return new CashMovementService(new CashMovementRepository(db), new CashShiftRepository(db),
            new CashRegisterRepository(db), new UnitOfWork<BillingDbContext>(db), actor,
            Options.Create(new CashRegisterAccessSettings
            {
                CashierRoleIds = ["cashier"],
                ManagerRoleIds = ["manager"]
            }), TimeProvider.System);
    }

    internal static CreateCashMovementDTO Body(string type, decimal? amount)
    {
        return new CreateCashMovementDTO
        {
            Type = type,
            Amount = amount,
            Reason = "Cash for supplies"
        };
    }

    private static async Task Status(int expected, Func<Task> action)
    {
        Assert.Equal(expected, (await Assert.ThrowsAsync<BillingException>(action)).StatusCode);
    }
}
