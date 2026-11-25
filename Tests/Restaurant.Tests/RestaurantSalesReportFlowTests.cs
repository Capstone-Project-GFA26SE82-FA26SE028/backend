extern alias billingapi;

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Persistence.RestaurantIsolation;
using Xunit;
using BillingDb = Billing.DAL.Models.BillingDbContext;
using Payment = Billing.DAL.Models.Payment;
using Bill = Billing.DAL.Models.Bill;
using BillLine = Billing.DAL.Models.BillLine;
using CashShift = Billing.DAL.Models.CashShift;
using CashRegister = Billing.DAL.Models.CashRegister;
using OnlinePayment = Billing.DAL.Models.OnlinePayment;
using PaymentMethod = Billing.DAL.Models.PaymentMethod;

namespace Restaurant.Tests;

public sealed partial class GatewayFlowTests
{
    [Fact]
    public async Task SalesReportUsesRealRepositoryAndMapsKnownAmountsAndNullAcrossGrpc()
    {
        var restaurant = await CreateRestaurant();
        await AssignReportManager(restaurant.RestaurantId);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var tenant = new RestaurantDataScope();
        tenant.Select(restaurant.RestaurantId);
        await using (var db = new ReportSalesDb(connection, tenant))
        {
            await db.Database.EnsureCreatedAsync();
            SeedReportFinancialData(db, restaurant.RestaurantId);
            await db.SaveChangesAsync();
        }

        await using var billing = ReportBillingHost().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<BillingDb>();
                services.AddScoped<BillingDb>(provider => new ReportSalesDb(connection,
                    provider.GetRequiredService<RestaurantDataScope>()));
            });
        });
        await using var gateway = ReportGatewayHost(billing);
        using var client = gateway.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "manager");
        var path = ReportUrl(restaurant.RestaurantId).Replace("/reports/period", "/reports/sales");
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = json.RootElement.GetProperty("result");
        var totals = result.GetProperty("totals");
        Assert.Equal(158m, totals.GetProperty("collections").GetProperty("amount").GetDecimal());
        Assert.Equal(100m, totals.GetProperty("dishRevenue").GetProperty("knownAmount").GetDecimal());
        Assert.Equal(JsonValueKind.Null, totals.GetProperty("dishRevenue").GetProperty("amount").ValueKind);
        Assert.False(totals.GetProperty("dishRevenue").GetProperty("isComplete").GetBoolean());
        Assert.Equal(2, totals.GetProperty("paidOrders").GetProperty("count").GetInt32());
        Assert.Equal(JsonValueKind.Null, totals.GetProperty("netCollections").GetProperty("amount").ValueKind);
        Assert.Equal(0m, totals.GetProperty("refunds").GetProperty("knownAmount").GetDecimal());
        Assert.Single(result.GetProperty("groups").EnumerateArray());
        Assert.Contains(result.GetProperty("warnings").EnumerateArray(), value =>
            value.GetProperty("code").GetString() == "TakeawayRevenueBreakdownUnavailable");
        using var repeated = await client.GetAsync(path);
        Assert.Equal(await response.Content.ReadAsStringAsync(), await repeated.Content.ReadAsStringAsync());
        var next = path.Replace("2026-11-26", "2026-11-27").Replace("2026-11-25", "2026-11-26");
        using var refunded = await client.GetAsync(next);
        Assert.Equal(HttpStatusCode.OK, refunded.StatusCode);
        using var refundJson = JsonDocument.Parse(await refunded.Content.ReadAsStringAsync());
        var refundTotals = refundJson.RootElement.GetProperty("result").GetProperty("totals");
        Assert.Equal(50m, refundTotals.GetProperty("refunds").GetProperty("knownAmount").GetDecimal());
        Assert.Equal(-50m, refundTotals.GetProperty("netCollections").GetProperty("knownAmount").GetDecimal());
        await using var verify = new ReportSalesDb(connection, tenant);
        Assert.Equal(1, await verify.Payments.CountAsync());
        Assert.Equal(1, await verify.OnlinePayments.CountAsync());
        Assert.Equal("Refunded", (await verify.OnlinePayments.SingleAsync()).Status);
    }

    [Theory]
    [InlineData("methodCode=Cash", 0, 0)]
    [InlineData("methodCode=BankTransfer", 158, 2)]
    [InlineData("shift=unassigned", 50, 1)]
    [InlineData("shift=own", 108, 1)]
    [InlineData("shift=11111111-1111-1111-1111-111111111111", 0, 0)]
    public async Task SalesReportFiltersApplyToRealRepositoryAndAllBreakdowns(string filter, decimal amount, int count)
    {
        var restaurant = await CreateRestaurant();
        await AssignReportManager(restaurant.RestaurantId);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var tenant = new RestaurantDataScope();
        tenant.Select(restaurant.RestaurantId);
        await using (var db = new ReportSalesDb(connection, tenant))
        {
            await db.Database.EnsureCreatedAsync();
            SeedReportFinancialData(db, restaurant.RestaurantId);
            await db.SaveChangesAsync();
        }

        await using var billing = ReportBillingHost().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<BillingDb>();
                services.AddScoped<BillingDb>(provider => new ReportSalesDb(connection,
                    provider.GetRequiredService<RestaurantDataScope>()));
            });
        });
        await using var gateway = ReportGatewayHost(billing);
        using var client = gateway.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "manager");
        await using var read = new ReportSalesDb(connection, tenant);
        var shift = await read.Payments.Select(value => value.CashShiftId).SingleAsync();
        filter = filter.Replace("shift=own", "shift=" + shift);
        var path = ReportUrl(restaurant.RestaurantId).Replace("/reports/period", "/reports/sales") + "&" + filter;
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = json.RootElement.GetProperty("result");
        Assert.Equal(amount, result.GetProperty("totals").GetProperty("collections").GetProperty("amount").GetDecimal());
        Assert.Equal(count, result.GetProperty("totals").GetProperty("paidOrders").GetProperty("count").GetInt32());
        foreach (var name in new[] { "channels", "methods", "shifts" })
        {
            Assert.Equal(amount, result.GetProperty(name).EnumerateArray()
                .Sum(value => value.GetProperty("totals").GetProperty("collections").GetProperty("knownAmount").GetDecimal()));
        }

        var next = path.Replace("2026-11-26", "2026-11-27").Replace("2026-11-25", "2026-11-26");
        using var refunded = await client.GetAsync(next);
        Assert.Equal(HttpStatusCode.OK, refunded.StatusCode);
        using var refundJson = JsonDocument.Parse(await refunded.Content.ReadAsStringAsync());
        var refundResult = refundJson.RootElement.GetProperty("result");
        var expectedRefund = filter is "methodCode=BankTransfer" or "shift=unassigned" ? 50 : 0;
        Assert.Equal(expectedRefund, refundResult.GetProperty("totals").GetProperty("refunds").GetProperty("knownAmount").GetDecimal());
        foreach (var name in new[] { "channels", "methods", "shifts" })
        {
            Assert.Equal(expectedRefund, refundResult.GetProperty(name).EnumerateArray()
                .Sum(value => value.GetProperty("totals").GetProperty("refunds").GetProperty("knownAmount").GetDecimal()));
        }
    }

    [Theory]
    [InlineData("DineIn", 0)]
    [InlineData("Takeaway", 50)]
    public async Task SalesReportKeepsUnknownChannelOutOfPureChannelResultsAndMarksItIncomplete(string channel, decimal amount)
    {
        var restaurant = await CreateRestaurant();
        await AssignReportManager(restaurant.RestaurantId);
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var tenant = new RestaurantDataScope();
        tenant.Select(restaurant.RestaurantId);
        await using (var db = new ReportSalesDb(connection, tenant))
        {
            await db.Database.EnsureCreatedAsync();
            SeedReportFinancialData(db, restaurant.RestaurantId);
            await db.SaveChangesAsync();
        }

        await using var billing = ReportBillingHost().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<BillingDb>();
                services.AddScoped<BillingDb>(provider => new ReportSalesDb(connection,
                    provider.GetRequiredService<RestaurantDataScope>()));
            });
        });
        await using var gateway = ReportGatewayHost(billing);
        using var client = gateway.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "manager");
        var path = ReportUrl(restaurant.RestaurantId).Replace("/reports/period", "/reports/sales");
        using var response = await client.GetAsync(path + "&channel=" + channel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = json.RootElement.GetProperty("result");
        Assert.Equal(amount, result.GetProperty("totals").GetProperty("collections").GetProperty("knownAmount").GetDecimal());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("totals").GetProperty("collections").GetProperty("amount").ValueKind);
        Assert.Contains(result.GetProperty("warnings").EnumerateArray(), value => value.GetProperty("code").GetString() == "OrderChannelUnavailable");
    }

    [Theory]
    [InlineData("", 401)]
    [InlineData("revoked", 401)]
    [InlineData("cashier", 403)]
    [InlineData("waiter", 403)]
    [InlineData("admin", 403)]
    [InlineData("restaurant-admin", 403)]
    public async Task SalesReportRejectsUnauthorizedActorsBeforeFinancialReads(string role, int expected)
    {
        var restaurant = await CreateRestaurant();
        await AssignReportManager(restaurant.RestaurantId);
        await using var billing = ReportBillingHost();
        await using var gateway = ReportGatewayHost(billing);
        using var client = gateway.CreateClient();
        if (role.Length > 0)
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", role);
        }

        using var response = await client.GetAsync(ReportUrl(restaurant.RestaurantId).Replace("/reports/period", "/reports/sales"));
        Assert.Equal(expected, (int)response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("result").ValueKind);
    }

    [Fact]
    public async Task SalesReportRejectsOtherRestaurantAndInvalidPeriodBeforeFinancialReads()
    {
        var assigned = await CreateRestaurant();
        var other = await CreateRestaurant();
        await AssignReportManager(assigned.RestaurantId);
        await using var billing = ReportBillingHost();
        await using var gateway = ReportGatewayHost(billing);
        using var client = gateway.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "manager");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync(ReportUrl(other.RestaurantId).Replace("/reports/period", "/reports/sales"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync($"/api/restaurants/{assigned.RestaurantId}/reports/sales?from=invalid&to=invalid")).StatusCode);
    }

    private static void SeedReportFinancialData(BillingDb db, Guid restaurantId)
    {
        var at = new DateTime(2026, 11, 25, 2, 0, 0, DateTimeKind.Utc);
        var bill = new Bill
        {
            BillId = Guid.NewGuid(),
            RestaurantId = restaurantId,
            BillNumber = "R2-DEMO",
            Status = "Paid",
            Currency = "VND",
            PaidAt = at,
            SubTotal = 100,
            VatAmount = 8,
            TotalAmount = 108
        };
        var orderId = Guid.NewGuid();
        for (var index = 0; index < 2; index++)
        {
            bill.BillLines.Add(new BillLine
            {
                BillLineId = Guid.NewGuid(),
                OrderItemRef = Guid.NewGuid(),
                TableSessionRef = Guid.NewGuid(),
                OriginalTableRef = Guid.NewGuid(),
                ItemSnapshot = JsonSerializer.Serialize(new
                {
                    OrderId = orderId
                }),
                Quantity = 1,
                UnitPriceSnapshot = 50,
                VatAllocated = 4
            });
        }

        db.PaymentMethods.Add(new PaymentMethod
        {
            MethodCode = "BankTransfer",
            IsEnabled = true
        });
        db.Payments.Add(new Payment
        {
            PaymentId = Guid.NewGuid(),
            Bill = bill,
            CashShift = new CashShift
            {
                CashShiftId = Guid.NewGuid(),
                CashRegister = new CashRegister
                {
                    CashRegisterId = Guid.NewGuid(),
                    RestaurantId = restaurantId,
                    Code = "MAIN"
                },
                CashierUserRef = Guid.NewGuid(),
                OpenedAt = at.AddHours(-1),
                Status = "Active"
            },
            MethodCode = "BankTransfer",
            Status = "Paid",
            Amount = 108,
            PaidAt = at,
            VerificationSource = "PayOSQuery",
            IdempotencyKey = "PayOS:R2",
            ProviderOrderCode = "1",
            ProviderPaymentLinkId = "link",
            ProviderTransactionId = "dine-in-reference"
        });
        db.OnlinePayments.Add(new OnlinePayment
        {
            OrderId = Guid.NewGuid(),
            RestaurantId = restaurantId,
            CustomerUserId = Guid.NewGuid(),
            ProviderOrderCode = 2,
            Amount = 50,
            Status = "Refunded",
            PaidAt = at.AddHours(1),
            RefundedAt = at.AddDays(1).AddHours(1),
            LinkId = "online-link",
            TransactionReference = "online-reference",
            RefundReference = "refund-reference",
            RefundVerifiedBy = Guid.NewGuid(),
            RefundEvidence = "https://example.test/refund",
            ExpiresAt = at.AddHours(2)
        });
    }

    private sealed class ReportSalesDb(SqliteConnection connection, RestaurantDataScope scope)
        : BillingDb(new DbContextOptionsBuilder<BillingDb>().UseSqlite(connection).Options, scope)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<Bill>().Property(value => value.Version).HasDefaultValueSql("X'0000000000000000'");
        }
    }
}
