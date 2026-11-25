extern alias billingapi;
extern alias orderingapi;

using System.Net;
using System.Text.Json;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Shared.Persistence.RestaurantIsolation;
using Xunit;
using OrderDb = Ordering.DAL.Models.OrderingDbContext;
using Order = Ordering.DAL.Models.Order;
using BillingDb = Billing.DAL.Models.BillingDbContext;
using Bill = Billing.DAL.Models.Bill;
using BillLine = Billing.DAL.Models.BillLine;
using Payment = Billing.DAL.Models.Payment;
using OrderingHost = orderingapi::Ordering.API.GrpcServices.BillingOrderGrpcService;
using OrderingRestaurant = orderingapi::Backend.Contracts.Restaurant.RestaurantAccessGrpc.RestaurantAccessGrpcClient;
using BillingOrders = billingapi::Backend.Contracts.Ordering.BillingOrderGrpc.BillingOrderGrpcClient;
using OrderRequest = billingapi::Backend.Contracts.Ordering.OrderReportChannelsRequest;

namespace Restaurant.Tests;

public sealed partial class GatewayFlowTests
{
    [Fact]
    public async Task SalesReportTraversesRealOrderingGrpcScopeAuthorizationAndRepository()
    {
        var restaurant = await CreateRestaurant();
        var other = await CreateRestaurant();
        await AssignReportManager(restaurant.RestaurantId);
        await using var billingConnection = new SqliteConnection("Data Source=:memory:");
        await using var orderingConnection = new SqliteConnection("Data Source=:memory:");
        await billingConnection.OpenAsync();
        await orderingConnection.OpenAsync();
        var tenant = new RestaurantDataScope();
        tenant.Select(restaurant.RestaurantId);
        Guid first;
        var mixed = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var foreign = Guid.NewGuid();
        await using (var db = new ReportSalesDb(billingConnection, tenant))
        {
            await db.Database.EnsureCreatedAsync();
            SeedReportFinancialData(db, restaurant.RestaurantId);
            first = JsonDocument.Parse(db.ChangeTracker.Entries<BillLine>().First().Entity.ItemSnapshot)
                .RootElement.GetProperty("OrderId").GetGuid();
            var original = db.ChangeTracker.Entries<Payment>().Single().Entity;
            AddReportBill(db, original, mixed);
            AddReportBill(db, original, [foreign]);
            await db.SaveChangesAsync();
        }

        await using (var db = new ReportOrderDb(orderingConnection))
        {
            await db.Database.EnsureCreatedAsync();
            db.Orders.AddRange(
                ReportOrder(first, restaurant.RestaurantId, "DineIn"),
                ReportOrder(mixed[0], restaurant.RestaurantId, "DineIn"),
                ReportOrder(mixed[1], restaurant.RestaurantId, "Takeaway"),
                ReportOrder(foreign, other.RestaurantId, "DineIn"));
            await db.SaveChangesAsync();
        }

        await using var ordering = ReportOrderingHost(orderingConnection);
        await using var billing = ReportBillingWithOrdering(billingConnection, ordering);
        await using var gateway = ReportGatewayHost(billing);
        using var client = gateway.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "manager");
        var path = ReportUrl(restaurant.RestaurantId).Replace("/reports/period", "/reports/sales");
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = json.RootElement.GetProperty("result");
        Assert.Equal(374m, result.GetProperty("totals").GetProperty("collections").GetProperty("amount").GetDecimal());
        var channels = result.GetProperty("channels").EnumerateArray().ToDictionary(value => value.GetProperty("key").GetString()!);
        Assert.Equal(108m, channels["DineIn"].GetProperty("totals").GetProperty("collections").GetProperty("knownAmount").GetDecimal());
        Assert.Equal(108m, channels["Mixed"].GetProperty("totals").GetProperty("collections").GetProperty("knownAmount").GetDecimal());
        Assert.Equal(108m, channels["Unclassified"].GetProperty("totals").GetProperty("collections").GetProperty("amount").GetDecimal());
        Assert.Equal(50m, channels["Takeaway"].GetProperty("totals").GetProperty("collections").GetProperty("knownAmount").GetDecimal());
        Assert.All(channels.Where(value => value.Key != "Unclassified"), value =>
            Assert.Equal(JsonValueKind.Null, value.Value.GetProperty("totals").GetProperty("collections").GetProperty("amount").ValueKind));

        using var grpc = GrpcChannel.ForAddress(ordering.Server.BaseAddress, new GrpcChannelOptions
        {
            HttpHandler = ordering.Server.CreateHandler()
        });
        var rpc = new BillingOrders(grpc);
        var request = new OrderRequest
        {
            RestaurantId = restaurant.RestaurantId.ToString()
        };
        request.OrderIds.Add(first.ToString());
        var denied = await Assert.ThrowsAsync<RpcException>(async () => await rpc.GetReportChannelsAsync(request,
            new Metadata
            {
                { "authorization", "Bearer cashier" }
            }));
        Assert.Equal(StatusCode.PermissionDenied, denied.StatusCode);

        await AssignReportManager(other.RestaurantId);
        var otherPath = ReportUrl(other.RestaurantId).Replace("/reports/period", "/reports/sales");
        client.DefaultRequestHeaders.Add("X-Restaurant-Id", restaurant.RestaurantId.ToString());
        using var mismatched = await client.GetAsync(otherPath);
        Assert.Equal(HttpStatusCode.Forbidden, mismatched.StatusCode);
        client.DefaultRequestHeaders.Remove("X-Restaurant-Id");
        client.DefaultRequestHeaders.Add("X-Restaurant-Id", other.RestaurantId.ToString());
        using var matched = await client.GetAsync(otherPath);
        Assert.Equal(HttpStatusCode.OK, matched.StatusCode);
        await using var verify = new ReportOrderDb(orderingConnection);
        Assert.Equal(4, await verify.Orders.CountAsync());
    }

    private WebApplicationFactory<OrderingHost> ReportOrderingHost(SqliteConnection orderingConnection)
    {
        return new WebApplicationFactory<OrderingHost>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=unused;Database=unused");
            builder.UseSetting("GrpcServices:Restaurant", "http://restaurant.test");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<OrderDb>();
                services.AddScoped<OrderDb>(provider => new ReportOrderDb(orderingConnection,
                    provider.GetRequiredService<RestaurantDataScope>()));
                services.AddGrpcClient<OrderingRestaurant>()
                    .ConfigurePrimaryHttpMessageHandler(() => _restaurant.Server.CreateHandler());
            });
        });
    }

    private WebApplicationFactory<billingapi::Billing.API.GrpcServices.RestaurantReportGrpcService> ReportBillingWithOrdering(
        SqliteConnection billingConnection, WebApplicationFactory<OrderingHost> ordering)
    {
        return ReportBillingHost().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<BillingDb>();
                services.AddScoped<BillingDb>(provider => new ReportSalesDb(billingConnection,
                    provider.GetRequiredService<RestaurantDataScope>()));
                services.RemoveAll<Billing.BLL.Interfaces.IRestaurantReportOrderClient>();
                services.AddScoped<Billing.BLL.Interfaces.IRestaurantReportOrderClient,
                    billingapi::Billing.API.Clients.RestaurantReportOrderClient>();
                services.RemoveAll<Billing.BLL.Interfaces.IRestaurantReportItemsClient>();
                services.AddScoped<Billing.BLL.Interfaces.IRestaurantReportItemsClient,
                    billingapi::Billing.API.Clients.RestaurantReportItemsClient>();
                services.AddGrpcClient<BillingOrders>()
                    .ConfigurePrimaryHttpMessageHandler(() => ordering.Server.CreateHandler());
            });
        });
    }

    private static void AddReportBill(BillingDb db, Payment original, Guid[] orderIds)
    {
        var bill = new Bill
        {
            BillId = Guid.NewGuid(),
            RestaurantId = original.Bill.RestaurantId,
            BillNumber = Guid.NewGuid().ToString("N"),
            Status = "Paid",
            Currency = "VND",
            PaidAt = original.PaidAt,
            SubTotal = 100,
            VatAmount = 8,
            TotalAmount = 108
        };
        foreach (var id in orderIds)
        {
            bill.BillLines.Add(new BillLine
            {
                BillLineId = Guid.NewGuid(),
                OrderItemRef = Guid.NewGuid(),
                TableSessionRef = Guid.NewGuid(),
                OriginalTableRef = Guid.NewGuid(),
                ItemSnapshot = JsonSerializer.Serialize(new
                {
                    OrderId = id
                }),
                Quantity = 1,
                UnitPriceSnapshot = 100m / orderIds.Length,
                VatAllocated = 8m / orderIds.Length
            });
        }

        db.Payments.Add(new Payment
        {
            PaymentId = Guid.NewGuid(),
            Bill = bill,
            CashShift = original.CashShift,
            MethodCode = "BankTransfer",
            Status = "Paid",
            Amount = 108,
            PaidAt = bill.PaidAt,
            VerificationSource = "PayOSQuery",
            ProviderOrderCode = Guid.NewGuid().ToString(),
            IdempotencyKey = "PayOS:" + Guid.NewGuid(),
            ProviderPaymentLinkId = "fixture-link",
            ProviderTransactionId = Guid.NewGuid().ToString()
        });
    }

    private static Order ReportOrder(Guid id, Guid restaurant, string channel)
    {
        return new Order
        {
            OrderId = id,
            RestaurantRef = restaurant,
            OrderNumber = id.ToString(),
            TableSessionRef = Guid.NewGuid(),
            Channel = channel,
            Status = "Confirmed",
            IdempotencyKey = id.ToString(),
            RequestHash = "fixture",
            CreatedAt = DateTime.UtcNow
        };
    }

    private sealed class ReportOrderDb : OrderDb
    {
        public ReportOrderDb(SqliteConnection connection) : base(Options(connection))
        {
        }

        public ReportOrderDb(SqliteConnection connection, RestaurantDataScope scope) : base(Options(connection), scope)
        {
        }

        private static DbContextOptions<OrderDb> Options(SqliteConnection connection)
        {
            return new DbContextOptionsBuilder<OrderDb>().UseSqlite(connection).Options;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Order>().Property(value => value.Version).HasDefaultValue(new byte[] { 1 });
            modelBuilder.Entity<Ordering.DAL.Models.OrderItem>().Property(value => value.Version).HasDefaultValue(new byte[] { 1 });
        }
    }
}
