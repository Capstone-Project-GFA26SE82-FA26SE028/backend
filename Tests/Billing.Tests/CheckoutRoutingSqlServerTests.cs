using System.Text.RegularExpressions;
using Billing.Common.Exceptions;
using Billing.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace Billing.Tests;

public sealed partial class BillSqlServerTests
{
    [BillingSqlServerFact]
    public async Task ConcurrentWaiterSubmissionsCannotRouteOneCheckoutToTwoRegisters()
    {
        var source = new BillServiceTests.BillSource();
        await using var seed = Context();
        var data = await CheckoutRoutingTests.Seed(seed, source);
        var otherId = Guid.NewGuid();
        seed.CashShifts.Add(new CashShift
        {
            CashShiftId = Guid.NewGuid(),
            CashRegister = new CashRegister
            {
                CashRegisterId = otherId,
                RestaurantId = source.Value.RestaurantId,
                Code = "SECOND"
            },
            CashierUserRef = data.CashierId,
            Status = "Active",
            OpenedAt = DateTime.UtcNow
        });
        await seed.SaveChangesAsync();
        async Task<int> Submit(Guid register)
        {
            await using var db = Context();
            var actor = CheckoutRoutingTests.Waiter(source, data.WaiterId);
            try
            {
                await CheckoutRoutingTests.Service(db, source, actor, data.WaiterId)
                    .Submit(source.Value.TableSessionId, data.CheckoutId, register, default);
                return 200;
            }
            catch (BillingException error)
            {
                return error.StatusCode;
            }
        }

        var results = await Task.WhenAll(Submit(data.RegisterId), Submit(otherId));
        Assert.Equal(new[] { 200, 409 }, results.Order().ToArray());
        seed.ChangeTracker.Clear();
        var stored = await seed.BillCheckouts.SingleAsync();
        Assert.Contains(stored.CashRegisterId, new Guid?[] { data.RegisterId, otherId });
        Assert.NotNull(stored.SubmittedAt);
        Assert.Empty(await seed.Bills.ToListAsync());
    }

    [BillingSqlServerFact]
    public async Task ConcurrentPreparationAndPresentationKeepOneBillAndStableAuditTimes()
    {
        var source = new BillServiceTests.BillSource();
        await using var seed = Context();
        var data = await CheckoutRoutingTests.Seed(seed, source);
        var waiter = CheckoutRoutingTests.Waiter(source, data.WaiterId);
        await CheckoutRoutingTests.Service(seed, source, waiter, data.WaiterId)
            .Submit(source.Value.TableSessionId, data.CheckoutId, data.RegisterId, default);
        async Task<Billing.Common.DTOs.BillCheckoutDTO> Prepare()
        {
            await using var db = Context();
            var cashier = CheckoutRoutingTests.Waiter(source, data.CashierId);
            cashier.Scope.RoleId = "cashier";
            return await CheckoutRoutingTests.Service(db, source, cashier, data.WaiterId)
                .Synchronize(source.Value.TableSessionId, data.CheckoutId, default);
        }

        var prepared = await Task.WhenAll(Prepare(), Prepare());
        Assert.All(prepared, value => Assert.Equal("Ready", value.Status));
        Assert.Equal(prepared[0].BillId, prepared[1].BillId);
        Assert.Equal(prepared[0].PreparedAt, prepared[1].PreparedAt);
        async Task<Billing.Common.DTOs.BillCheckoutDTO> Present()
        {
            await using var db = Context();
            return await CheckoutRoutingTests.Service(db, source, waiter, data.WaiterId)
                .Present(source.Value.TableSessionId, data.CheckoutId, default);
        }

        var presented = await Task.WhenAll(Present(), Present());
        Assert.NotNull(presented[0].PresentedAt);
        Assert.Equal(presented[0].PresentedAt, presented[1].PresentedAt);
        Assert.Single(await seed.Bills.AsNoTracking().ToListAsync());
        Assert.Single(await seed.BillLines.AsNoTracking().ToListAsync());
        Assert.Empty(await seed.Payments.ToListAsync());
    }

    [BillingSqlServerFact]
    public async Task RoutingUpgradeIsRepeatableAndLeavesLegacyRowsUnassigned()
    {
        var id = Guid.NewGuid();
        await Execute(_connection, $"INSERT dbo.BillCheckout (CheckoutId,TableSessionRef,Status,CreatedAt) VALUES ('{id}',NEWID(),'Preparing',SYSUTCDATETIME());");
        await Execute(_connection, "ALTER TABLE dbo.BillCheckout DROP CONSTRAINT FK_BillCheckout_CashRegister; ALTER TABLE dbo.BillCheckout DROP COLUMN CashRegisterId,SubmittedAt,PreparedByRef,PreparedAt,PresentedAt;");
        var upgrade = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Sql", "BillingCheckoutRouting.sql"));
        upgrade = Regex.Replace(upgrade, @"^\s*GO\s*$", string.Empty, RegexOptions.Multiline | RegexOptions.IgnoreCase);
        await Execute(_connection, upgrade);
        await Execute(_connection, upgrade);
        await using var db = Context();
        var stored = await db.BillCheckouts.SingleAsync();
        Assert.Equal(id, stored.CheckoutId);
        Assert.Null(stored.CashRegisterId);
        Assert.Null(stored.SubmittedAt);
        Assert.Null(stored.PreparedByRef);
        Assert.Null(stored.PreparedAt);
        Assert.Null(stored.PresentedAt);
    }
}
