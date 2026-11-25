using Billing.Common.DTOs;
using Billing.DAL.Implements;
using Billing.DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace Billing.Tests;

public sealed class RestaurantReportRepositoryTests
{
    [Fact]
    public async Task MissingTimeCountsRespectChannelMethodAndShiftFilters()
    {
        await using var fixture = await BillServiceTests.BillFixture.Create();
        var payment = RestaurantSalesReportServiceTests.Online(fixture.Source.Value.RestaurantId,
            RestaurantSalesReportServiceTests.At(3));
        payment.Status = "Refunded";
        payment.PaidAt = null;
        payment.RefundedAt = null;
        fixture.Db.OnlinePayments.Add(payment);
        await fixture.Db.SaveChangesAsync();
        var repository = new RestaurantReportRepository(fixture.Db);
        foreach (var filters in new[]
        {
            new SalesReportFilterDTO
            {
                MethodCode = "Cash"
            },
            new SalesReportFilterDTO
            {
                Shift = Guid.NewGuid().ToString()
            }
        })
        {
            var result = await repository.GetSourceAsync(fixture.Source.Value.RestaurantId,
                RestaurantSalesReportServiceTests.At(0), RestaurantSalesReportServiceTests.At(0).AddDays(1), default, filters);
            Assert.Equal(0, result.MissingPaymentTimes);
            Assert.Equal(0, result.MissingRefundTimes);
        }

        var unassigned = await repository.GetSourceAsync(fixture.Source.Value.RestaurantId,
            RestaurantSalesReportServiceTests.At(0), RestaurantSalesReportServiceTests.At(0).AddDays(1), default,
            new SalesReportFilterDTO
            {
                Shift = "unassigned"
            });
        Assert.Equal(1, unassigned.MissingPaymentTimes);
        Assert.Equal(1, unassigned.MissingRefundTimes);
    }

    [Fact]
    public async Task DineInReadsRespectTenantAndHalfOpenBoundsWithoutTrackingOrMultiplication()
    {
        await using var fixture = await CashPaymentServiceTests.Fixture.Create(withHandover: true);
        await fixture.Pay(300);
        var stored = await fixture.Db.Payments.SingleAsync();
        var instant = stored.PaidAt!.Value;
        fixture.Db.ChangeTracker.Clear();
        var repository = new RestaurantReportRepository(fixture.Db);
        var included = await repository.GetSourceAsync(fixture.Bill.RestaurantId, instant, instant.AddSeconds(1), default);
        var payment = Assert.Single(included.Payments);
        Assert.Single(payment.CashMovements);
        Assert.NotNull(payment.CashHandover);
        Assert.Single(payment.Bill.Payments);
        Assert.Single(payment.Bill.BillLines);
        Assert.Empty(fixture.Db.ChangeTracker.Entries());
        var excluded = await repository.GetSourceAsync(fixture.Bill.RestaurantId, instant.AddSeconds(-1), instant, default);
        Assert.Empty(excluded.Payments);
        var otherRestaurant = await repository.GetSourceAsync(Guid.NewGuid(), instant, instant.AddSeconds(1), default);
        Assert.Empty(otherRestaurant.Payments);
        Assert.Equal("Paid", (await fixture.Db.Payments.AsNoTracking().SingleAsync()).Status);
        Assert.Equal(1, await fixture.Db.CashMovements.CountAsync());
    }

    [Fact]
    public async Task RefundPeriodUsesRefundedAtRatherThanPaidAtAndFiltersOtherRestaurants()
    {
        await using var fixture = await BillServiceTests.BillFixture.Create();
        var instant = RestaurantSalesReportServiceTests.At(3);
        var inside = RestaurantSalesReportServiceTests.Online(fixture.Source.Value.RestaurantId, instant.AddDays(-1));
        inside.Status = "Refunded";
        inside.RefundedAt = instant;
        inside.ProviderOrderCode = 100;
        var end = RestaurantSalesReportServiceTests.Online(fixture.Source.Value.RestaurantId, instant.AddDays(-1));
        end.Status = "Refunded";
        end.RefundedAt = instant.AddSeconds(1);
        end.ProviderOrderCode = 101;
        var other = RestaurantSalesReportServiceTests.Online(Guid.NewGuid(), instant.AddDays(-1));
        other.Status = "Refunded";
        other.RefundedAt = instant;
        other.ProviderOrderCode = 102;
        fixture.Db.OnlinePayments.AddRange(inside, end, other);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var result = await new RestaurantReportRepository(fixture.Db).GetSourceAsync(
            fixture.Source.Value.RestaurantId, instant, instant.AddSeconds(1), default);
        Assert.Equal(inside.OrderId, Assert.Single(result.OnlineRefunds).OrderId);
        Assert.Empty(result.OnlineSales);
        Assert.Empty(fixture.Db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task MissingDatesAreReportedWithoutInventingAPeriod()
    {
        await using var fixture = await BillServiceTests.BillFixture.Create();
        var payment = RestaurantSalesReportServiceTests.Online(fixture.Source.Value.RestaurantId,
            RestaurantSalesReportServiceTests.At(3));
        payment.Status = "Refunded";
        payment.PaidAt = null;
        payment.RefundedAt = null;
        fixture.Db.OnlinePayments.Add(payment);
        await fixture.Db.SaveChangesAsync();
        var result = await new RestaurantReportRepository(fixture.Db).GetSourceAsync(
            fixture.Source.Value.RestaurantId, RestaurantSalesReportServiceTests.At(0), RestaurantSalesReportServiceTests.At(0).AddDays(1), default);
        Assert.Equal(1, result.MissingPaymentTimes);
        Assert.Equal(1, result.MissingRefundTimes);
        Assert.Empty(result.OnlineSales);
        Assert.Empty(result.OnlineRefunds);
    }
}
