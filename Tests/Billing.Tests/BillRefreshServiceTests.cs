using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace Billing.Tests;

public sealed class BillRefreshServiceTests
{
    [Fact]
    public async Task RefreshUsesOrderSnapshotsRetainsRatesAndStableLineIdsWithoutDuplicates()
    {
        await using var fixture = await BillServiceTests.BillFixture.Create();
        var created = await fixture.Service.Create(fixture.Source.Value.TableSessionId, default);
        var existing = Assert.Single(created.Items);
        fixture.Source.Value.Items[0].Quantity = 2;
        fixture.Source.Value.Items.Add(NewLine(fixture.Source.Value.Items[0]));
        var policy = await fixture.Db.TaxFeePolicies.SingleAsync();
        policy.VatRate = 0.9m;
        policy.ServiceRate = 0.9m;
        await fixture.Db.SaveChangesAsync();
        var refreshed = await fixture.Service.Refresh(created.BillId, default);
        Assert.Equal(422.50m, refreshed.SubTotal);
        Assert.Equal(21.13m, refreshed.ServiceAmount);
        Assert.Equal(44.36m, refreshed.VatAmount);
        Assert.Equal(488m, refreshed.TotalAmount);
        Assert.Equal(created.VatRateSnapshot, refreshed.VatRateSnapshot);
        Assert.Equal(created.ServiceRateSnapshot, refreshed.ServiceRateSnapshot);
        Assert.Equal(existing.BillLineId, refreshed.Items.Single(line => line.OrderItemRef == existing.OrderItemRef).BillLineId);
        Assert.Equal("MatchedAtRefresh", refreshed.SourceStatus);
        Assert.All(refreshed.Items, line => Assert.Null(line.CogsSnapshot));
        var repeated = await fixture.Service.Refresh(created.BillId, default);
        Assert.Equal(refreshed.TotalAmount, repeated.TotalAmount);
        Assert.Equal(2, await fixture.Db.BillLines.CountAsync());
        Assert.Equal("Current", (await fixture.Service.GetById(created.BillId, default)).SourceStatus);
    }

    [Fact]
    public async Task PartialAndFullCancellationKeepZeroLinesAndSessionLink()
    {
        await using var fixture = await BillServiceTests.BillFixture.Create();
        fixture.Source.Value.Items[0].Quantity = 3;
        var created = await fixture.Service.Create(fixture.Source.Value.TableSessionId, default);
        fixture.Source.Value.Items[0].Quantity = 1;
        var partial = await fixture.Service.Refresh(created.BillId, default);
        Assert.Equal(201.25m, partial.SubTotal);
        fixture.Source.Value.Items.Clear();
        var empty = await fixture.Service.Refresh(created.BillId, default);
        Assert.Equal(0, empty.TotalAmount);
        var cancelled = Assert.Single(empty.Items);
        Assert.Equal(0, cancelled.Quantity);
        Assert.Equal(created.Items[0].BillLineId, cancelled.BillLineId);
        Assert.Equal("Cancelled", cancelled.Item.Status);
        Assert.Equal("Current", (await fixture.Service.GetById(created.BillId, default)).SourceStatus);
        Assert.Equal(0, (await fixture.Service.Refresh(created.BillId, default)).TotalAmount);
        await Error(409, () => fixture.Service.Create(fixture.Source.Value.TableSessionId, default));
        fixture.Source.Value.Items.Add(NewLine(new BillSourceLineDTO()));
        var added = await fixture.Service.Refresh(created.BillId, default);
        Assert.Equal(20, added.SubTotal);
        Assert.Equal(2, added.Items.Count);
        Assert.Single(added.Items, line => line.Quantity == 0);
    }

    [Theory]
    [InlineData("Paid")]
    [InlineData("Cancelled")]
    [InlineData("Refunded")]
    public async Task NonUnpaidBillsCannotChange(string status)
    {
        await using var fixture = await BillServiceTests.BillFixture.Create();
        var created = await fixture.Service.Create(fixture.Source.Value.TableSessionId, default);
        var bill = await fixture.Db.Bills.SingleAsync();
        bill.Status = status;
        await fixture.Db.SaveChangesAsync();
        fixture.Source.Value.Items[0].Quantity++;
        await Error(409, () => fixture.Service.Refresh(created.BillId, default));
        Assert.Equal(created.TotalAmount, bill.TotalAmount);
    }

    [Theory]
    [InlineData("admin", true)]
    [InlineData("manager", false)]
    [InlineData("waiter", false)]
    [InlineData("kitchen", false)]
    public async Task RefreshDoesNotGrantOtherRolesWriteAccess(string role, bool administrator)
    {
        await using var fixture = await BillServiceTests.BillFixture.Create();
        var bill = await fixture.Service.Create(fixture.Source.Value.TableSessionId, default);
        fixture.Actor.Scope.RoleId = role;
        fixture.Actor.Scope.IsAdministrator = administrator;
        await Error(403, () => fixture.Service.Refresh(bill.BillId, default));
    }

    [Fact]
    public async Task CrossBranchUnknownIdEmptyIdAndRevokedAssignmentAreRejected()
    {
        await using var fixture = await BillServiceTests.BillFixture.Create();
        var bill = await fixture.Service.Create(fixture.Source.Value.TableSessionId, default);
        await Error(400, () => fixture.Service.Refresh(Guid.Empty, default));
        await Error(404, () => fixture.Service.Refresh(Guid.NewGuid(), default));
        fixture.Actor.Scope.RestaurantIds = [Guid.NewGuid()];
        await Error(403, () => fixture.Service.Refresh(bill.BillId, default));
        fixture.Actor.Scope.RestaurantIds = [];
        await Error(403, () => fixture.Service.Refresh(bill.BillId, default));
    }

    [Fact]
    public async Task PendingDuplicateInvalidAndChangingSourcesLeaveStoredBillUnchanged()
    {
        await using var fixture = await BillServiceTests.BillFixture.Create();
        var bill = await fixture.Service.Create(fixture.Source.Value.TableSessionId, default);
        fixture.Source.Value.PendingOrderCount = 1;
        await Error(409, () => fixture.Service.Refresh(bill.BillId, default));
        fixture.Source.Value.PendingOrderCount = 0;
        fixture.Source.Value.Items.Add(fixture.Source.Value.Items[0]);
        await Error(503, () => fixture.Service.Refresh(bill.BillId, default));
        fixture.Source.Value.Items.RemoveAt(1);
        fixture.Source.ChangeOnCall = fixture.Source.Calls + 2;
        await Error(409, () => fixture.Service.Refresh(bill.BillId, default));
        Assert.Equal(bill.SubTotal, (await fixture.Db.Bills.AsNoTracking().SingleAsync()).SubTotal);
        Assert.Equal(bill.Items[0].Quantity, (await fixture.Db.BillLines.AsNoTracking().SingleAsync()).Quantity);
    }

    [Fact]
    public async Task PaidTimestampAndDiscountedBillsAreNotRefreshed()
    {
        await using var fixture = await BillServiceTests.BillFixture.Create();
        var bill = await fixture.Service.Create(fixture.Source.Value.TableSessionId, default);
        var stored = await fixture.Db.Bills.SingleAsync();
        stored.PaidAt = DateTime.UtcNow;
        await fixture.Db.SaveChangesAsync();
        await Error(409, () => fixture.Service.Refresh(bill.BillId, default));
        stored.PaidAt = null;
        stored.DiscountAmount = 1;
        await fixture.Db.SaveChangesAsync();
        await Error(409, () => fixture.Service.Refresh(bill.BillId, default));
    }

    [Fact]
    public async Task UnavailableSourceAndLegacySnapshotCannotBeOverwritten()
    {
        await using var fixture = await BillServiceTests.BillFixture.Create();
        var bill = await fixture.Service.Create(fixture.Source.Value.TableSessionId, default);
        fixture.Source.Error = new BillingException(503, "Source unavailable.");
        await Error(503, () => fixture.Service.Refresh(bill.BillId, default));
        fixture.Source.Error = null;
        var stored = await fixture.Db.BillLines.SingleAsync();
        stored.ItemSnapshot = "{}";
        await fixture.Db.SaveChangesAsync();
        await Error(409, () => fixture.Service.Refresh(bill.BillId, default));
    }

    [Fact]
    public async Task ExistingPaymentRecordBlocksRefreshWithoutChangingBill()
    {
        await using var fixture = await BillServiceTests.BillFixture.Create();
        var bill = await fixture.Service.Create(fixture.Source.Value.TableSessionId, default);
        var method = new PaymentMethod
        {
            MethodCode = "FixtureCash",
            IsEnabled = true
        };
        var register = new CashRegister
        {
            CashRegisterId = Guid.NewGuid(),
            RestaurantId = bill.RestaurantId,
            Code = "Fixture"
        };
        var shift = new CashShift
        {
            CashShiftId = Guid.NewGuid(),
            CashRegister = register,
            CashierUserRef = fixture.Actor.Scope.UserId,
            Status = "Active",
            OpenedAt = DateTime.UtcNow
        };
        fixture.Db.Payments.Add(new Payment
        {
            PaymentId = Guid.NewGuid(),
            BillId = bill.BillId,
            CashShift = shift,
            MethodCodeNavigation = method,
            Status = "FixturePending",
            Amount = bill.TotalAmount,
            IdempotencyKey = Guid.NewGuid().ToString()
        });
        await fixture.Db.SaveChangesAsync();
        fixture.Source.Value.Items.Clear();
        await Error(409, () => fixture.Service.Refresh(bill.BillId, default));
        Assert.Equal(bill.TotalAmount, (await fixture.Db.Bills.SingleAsync()).TotalAmount);
    }

    [Fact]
    public async Task ChangedEmployeeSessionAndRevokedTargetScopeCannotWrite()
    {
        await using var fixture = await BillServiceTests.BillFixture.Create();
        var bill = await fixture.Service.Create(fixture.Source.Value.TableSessionId, default);
        fixture.Actor.TargetScope = new BillingAccessScope
        {
            UserId = Guid.NewGuid(),
            RoleId = "cashier",
            RestaurantIds = [bill.RestaurantId]
        };
        await Error(401, () => fixture.Service.Refresh(bill.BillId, default));
        fixture.Actor.TargetScope.UserId = fixture.Actor.Scope.UserId;
        fixture.Actor.TargetScope.RestaurantIds = [];
        await Error(403, () => fixture.Service.Refresh(bill.BillId, default));
    }

    [Fact]
    public async Task SourceBranchIdentityAndStorageOverflowAreRejected()
    {
        await using var fixture = await BillServiceTests.BillFixture.Create();
        var bill = await fixture.Service.Create(fixture.Source.Value.TableSessionId, default);
        fixture.Source.Value.RestaurantId = Guid.NewGuid();
        await Error(409, () => fixture.Service.Refresh(bill.BillId, default));
        fixture.Source.Value.RestaurantId = bill.RestaurantId;
        var original = fixture.Source.Value.Items[0].Item.ItemId;
        fixture.Source.Value.Items[0].Item.ItemId = Guid.NewGuid();
        await Error(409, () => fixture.Service.Refresh(bill.BillId, default));
        fixture.Source.Value.Items[0].Item.ItemId = original;
        fixture.Source.Value.Items[0].Item.BaseUnitPrice = 9999999999999999.99m;
        await Error(409, () => fixture.Service.Refresh(bill.BillId, default));
        Assert.Equal(bill.TotalAmount, (await fixture.Db.Bills.AsNoTracking().SingleAsync()).TotalAmount);
    }

    private static BillSourceLineDTO NewLine(BillSourceLineDTO existing)
    {
        return new BillSourceLineDTO
        {
            OrderItemId = Guid.NewGuid(),
            OriginalTableId = existing.OriginalTableId == Guid.Empty ? Guid.NewGuid() : existing.OriginalTableId,
            Quantity = 2,
            Item = new BillItemSnapshot
            {
                OrderId = existing.Item.OrderId == Guid.Empty ? Guid.NewGuid() : existing.Item.OrderId,
                ItemId = Guid.NewGuid(),
                Name = "Snapshot drink",
                Status = "Served",
                BaseUnitPrice = 10
            }
        };
    }

    private static async Task Error(int status, Func<Task> action)
    {
        Assert.Equal(status, (await Assert.ThrowsAsync<BillingException>(action)).StatusCode);
    }
}
