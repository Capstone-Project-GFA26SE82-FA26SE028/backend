using Billing.BLL.Implements;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.Common.Settings;
using Billing.DAL.Implements;
using Billing.DAL.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Common.Billing;
using Shared.Persistence.UnitOfWorks;

namespace Billing.Tests;

public sealed class CheckoutRoutingTests
{
    [Fact]
    public async Task ResponsibleWaiterRoutesCashierPreparesAndWaiterPresentsSameBill()
    {
        await using var core = await BillServiceTests.BillFixture.Create();
        var data = await Seed(core.Db, core.Source);
        var actor = Waiter(core.Source, data.WaiterId);
        var service = Service(core.Db, core.Source, actor, data.WaiterId);
        var submitted = await service.Submit(core.Source.Value.TableSessionId, data.CheckoutId, data.RegisterId, default);
        Assert.Equal(data.RegisterId, submitted.CashRegisterId);
        Assert.NotNull(submitted.SubmittedAt);
        Assert.Null(submitted.PreparedAt);
        Assert.Empty(await core.Db.Bills.ToListAsync());
        await Error(403, () => service.Synchronize(submitted.TableSessionId, data.CheckoutId, default));
        await Error(409, () => service.Present(submitted.TableSessionId, data.CheckoutId, default));
        actor.Scope.RoleId = "cashier";
        actor.Scope.UserId = data.CashierId;
        var inbox = await service.GetIncoming(data.RegisterId, 1, 20, default);
        Assert.Equal(data.CheckoutId, Assert.Single(inbox.Items).CheckoutId);
        var ready = await service.Synchronize(submitted.TableSessionId, data.CheckoutId, default);
        Assert.Equal("Ready", ready.Status);
        Assert.Equal(data.CashierId, ready.PreparedByRef);
        Assert.NotNull(ready.PreparedAt);
        Assert.Null(ready.PresentedAt);
        var repeated = await service.Synchronize(submitted.TableSessionId, data.CheckoutId, default);
        Assert.Equal(ready.PreparedAt, repeated.PreparedAt);
        Assert.Single(await core.Db.Bills.ToListAsync());
        actor.Scope.RoleId = "waiter";
        actor.Scope.UserId = data.WaiterId;
        var bills = Bills(core.Db, core.Source, actor, data.WaiterId);
        var prebill = await bills.GetPrebill(submitted.TableSessionId, data.CheckoutId, default);
        Assert.Equal(ready.BillId, prebill.BillId);
        Assert.Equal("Current", prebill.SourceStatus);
        Assert.Equal(232, prebill.TotalAmount);
        await Error(403, () => bills.GetById(prebill.BillId, default));
        var presented = await service.Present(submitted.TableSessionId, data.CheckoutId, default);
        Assert.NotNull(presented.PresentedAt);
        Assert.Equal(presented.PresentedAt, (await service.Present(submitted.TableSessionId, data.CheckoutId, default)).PresentedAt);
        Assert.Equal("Unpaid", (await core.Db.Bills.SingleAsync()).Status);
        Assert.Empty(await core.Db.Payments.ToListAsync());
        actor.Scope.UserId = Guid.NewGuid();
        await Error(403, () => bills.GetPrebill(submitted.TableSessionId, data.CheckoutId, default));
        await Error(403, () => service.Present(submitted.TableSessionId, data.CheckoutId, default));
    }

    [Fact]
    public async Task SubmitIsOwnerScopedIdempotentAndCannotMoveToAnotherRegister()
    {
        await using var core = await BillServiceTests.BillFixture.Create();
        var data = await Seed(core.Db, core.Source);
        var actor = Waiter(core.Source, Guid.NewGuid());
        var service = Service(core.Db, core.Source, actor, data.WaiterId);
        var session = core.Source.Value.TableSessionId;
        await Error(403, () => service.Submit(session, data.CheckoutId, data.RegisterId, default));
        actor.Scope.UserId = data.WaiterId;
        actor.Scope.RestaurantIds = [Guid.NewGuid()];
        await Error(403, () => service.Submit(session, data.CheckoutId, data.RegisterId, default));
        actor.Scope.RestaurantIds = [core.Source.Value.RestaurantId];
        var first = await service.Submit(session, data.CheckoutId, data.RegisterId, default);
        var retry = await service.Submit(session, data.CheckoutId, data.RegisterId, default);
        Assert.Equal(first.SubmittedAt, retry.SubmittedAt);
        await Error(409, () => service.Submit(session, data.CheckoutId, Guid.NewGuid(), default));
        Assert.Equal(data.RegisterId, (await core.Db.BillCheckouts.SingleAsync()).CashRegisterId);
    }

    [Fact]
    public async Task SubmitRejectsWrongRestaurantClosedShiftAndUnreviewedRequest()
    {
        await using var core = await BillServiceTests.BillFixture.Create();
        var data = await Seed(core.Db, core.Source);
        var actor = Waiter(core.Source, data.WaiterId);
        var requests = new TestPaymentRequestClient
        {
            ReviewedWaiterId = data.WaiterId,
            Reviewed = false
        };
        var service = Service(core.Db, core.Source, actor, data.WaiterId, requests);
        var session = core.Source.Value.TableSessionId;
        await Error(409, () => service.Submit(session, data.CheckoutId, data.RegisterId, default));
        requests.Reviewed = true;
        var register = await core.Db.CashRegisters.SingleAsync();
        register.RestaurantId = Guid.NewGuid();
        await core.Db.SaveChangesAsync();
        await Error(403, () => service.Submit(session, data.CheckoutId, data.RegisterId, default));
        register.RestaurantId = core.Source.Value.RestaurantId;
        var shift = await core.Db.CashShifts.SingleAsync();
        shift.Status = "Closed";
        shift.ClosedAt = DateTime.UtcNow;
        await core.Db.SaveChangesAsync();
        await Error(409, () => service.Submit(session, data.CheckoutId, data.RegisterId, default));
        Assert.Null((await core.Db.BillCheckouts.SingleAsync()).SubmittedAt);
        Assert.Empty(await core.Db.Bills.ToListAsync());
    }

    [Fact]
    public async Task OtherCashierCannotSeeInboxOrPrepareAndUnsubmittedCheckoutCannotCreateBill()
    {
        await using var core = await BillServiceTests.BillFixture.Create();
        var data = await Seed(core.Db, core.Source);
        var actor = Waiter(core.Source, data.CashierId);
        actor.Scope.RoleId = "cashier";
        var service = Service(core.Db, core.Source, actor, data.WaiterId);
        var session = core.Source.Value.TableSessionId;
        await Error(409, () => service.Synchronize(session, data.CheckoutId, default));
        actor.Scope.UserId = data.WaiterId;
        actor.Scope.RoleId = "waiter";
        await service.Submit(session, data.CheckoutId, data.RegisterId, default);
        actor.Scope.RoleId = "cashier";
        actor.Scope.UserId = Guid.NewGuid();
        await Error(403, () => service.GetIncoming(data.RegisterId, 1, 20, default));
        await Error(403, () => service.Synchronize(session, data.CheckoutId, default));
        Assert.Empty(await core.Db.Bills.ToListAsync());
    }

    [Fact]
    public async Task EligibleRegisterQueryUsesActiveShiftAndKeepsGeneralRegisterRightsUnchanged()
    {
        await using var core = await BillServiceTests.BillFixture.Create();
        var data = await Seed(core.Db, core.Source);
        core.Db.CashRegisters.Add(new CashRegister
        {
            CashRegisterId = Guid.NewGuid(),
            RestaurantId = core.Source.Value.RestaurantId,
            Code = "CLOSED"
        });
        await core.Db.SaveChangesAsync();
        var actor = Waiter(core.Source, data.WaiterId);
        var service = new CashRegisterService(new CashRegisterRepository(core.Db), new UnitOfWork<BillingDbContext>(core.Db), actor, Roles());
        var page = await service.GetAvailableForCheckout(new CashRegisterQueryDTO
        {
            RestaurantId = core.Source.Value.RestaurantId
        }, default);
        Assert.Equal(data.RegisterId, Assert.Single(page.Items).CashRegisterId);
        await Error(403, () => service.GetAll(new CashRegisterQueryDTO(), default));
        actor.Scope.RoleId = "cashier";
        await Error(403, () => service.GetAvailableForCheckout(new CashRegisterQueryDTO
        {
            RestaurantId = core.Source.Value.RestaurantId
        }, default));
    }

    internal static async Task<(Guid CheckoutId, Guid RegisterId, Guid WaiterId, Guid CashierId)> Seed(BillingDbContext db, BillServiceTests.BillSource source)
    {
        var waiterId = Guid.NewGuid();
        var cashierId = Guid.NewGuid();
        var registerId = Guid.NewGuid();
        var checkoutId = Guid.NewGuid();
        db.CashShifts.Add(new CashShift
        {
            CashShiftId = Guid.NewGuid(),
            CashRegister = new CashRegister
            {
                CashRegisterId = registerId,
                RestaurantId = source.Value.RestaurantId,
                Code = "ROUTING"
            },
            CashierUserRef = cashierId,
            Status = "Active",
            OpeningCash = 100000,
            OpenedAt = DateTime.UtcNow
        });
        db.BillCheckouts.Add(new BillCheckout
        {
            CheckoutId = checkoutId,
            TableSessionRef = source.Value.TableSessionId,
            Status = "Frozen",
            SourceHash = Hash(source),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return (checkoutId, registerId, waiterId, cashierId);
    }

    internal static ShiftIdentity Waiter(BillServiceTests.BillSource source, Guid userId)
    {
        return new ShiftIdentity
        {
            Scope = new BillingAccessScope
            {
                UserId = userId,
                RoleId = "waiter",
                RestaurantIds = [source.Value.RestaurantId]
            }
        };
    }

    internal static BillCheckoutService Service(BillingDbContext db, BillServiceTests.BillSource source, ShiftIdentity actor,
        Guid waiterId, TestPaymentRequestClient? requests = null)
    {
        requests ??= new TestPaymentRequestClient
        {
            ReviewedWaiterId = waiterId
        };
        return new BillCheckoutService(new BillCheckoutRepository(db), new BillRepository(db),
            new UnitOfWork<BillingDbContext>(db), new Remote(source), source, Bills(db, source, actor, waiterId),
            actor, Roles(), TimeProvider.System, requests, new CashRegisterRepository(db), new CashShiftRepository(db));
    }

    internal static BillService Bills(BillingDbContext db, BillServiceTests.BillSource source, ShiftIdentity actor, Guid waiterId)
    {
        return new BillService(new BillRepository(db), new TaxFeePolicyRepository(db), new UnitOfWork<BillingDbContext>(db),
            actor, source, Roles(), Options.Create(new BillSettings
            {
                Currency = "VND",
                OrderPriceBasis = "Exclusive"
            }), TimeProvider.System, new TestPaymentRequestClient
            {
                ReviewedWaiterId = waiterId
            });
    }

    private static IOptions<CashRegisterAccessSettings> Roles()
    {
        return Options.Create(new CashRegisterAccessSettings
        {
            CashierRoleIds = ["cashier"],
            WaiterRoleIds = ["waiter"]
        });
    }

    private static async Task Error(int status, Func<Task> action)
    {
        Assert.Equal(status, (await Assert.ThrowsAsync<BillingException>(action)).StatusCode);
    }

    private static string Hash(BillServiceTests.BillSource source)
    {
        return CheckoutHash.Compute(source.Value.Items.Select(line => new CheckoutHashLine(line.OrderItemId,
            line.Item.ItemId, line.Quantity, line.Item.BaseUnitPrice, line.Item.Options.Select(option =>
                new CheckoutHashOption(option.OptionId, option.Quantity, option.ExtraPrice)).ToArray())));
    }

    private sealed class Remote(BillServiceTests.BillSource source) : ICheckoutSourceClient
    {
        public Task VerifyAsync(Guid sessionId, string guestToken, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task<BillCheckoutDTO> BeginAsync(Guid sessionId, Guid id, string guestToken, CancellationToken ct)
        {
            return Task.FromResult(new BillCheckoutDTO
            {
                CheckoutId = id,
                TableSessionId = sessionId,
                Status = "Frozen",
                SourceHash = Hash(source)
            });
        }

        public Task BindAsync(Guid sessionId, Guid id, Guid billId, string hash, CancellationToken ct)
        {
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(Guid sessionId, Guid id, CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }
}
