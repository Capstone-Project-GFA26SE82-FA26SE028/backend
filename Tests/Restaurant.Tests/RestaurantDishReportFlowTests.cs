extern alias billingapi;

using Grpc.Core;
using Grpc.Net.Client;
using System.Net;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shared.Persistence.RestaurantIsolation;
using Xunit;
using BillingDb = Billing.DAL.Models.BillingDbContext;
using Bill = Billing.DAL.Models.Bill;
using BillLine = Billing.DAL.Models.BillLine;
using Payment = Billing.DAL.Models.Payment;
using PaymentMethod = Billing.DAL.Models.PaymentMethod;
using CashShift = Billing.DAL.Models.CashShift;
using CashRegister = Billing.DAL.Models.CashRegister;
using OrderItem = Ordering.DAL.Models.OrderItem;
using OrderItemOption = Ordering.DAL.Models.OrderItemOption;

namespace Restaurant.Tests;

public sealed partial class GatewayFlowTests
{
    [Fact]
    public async Task DishReportUsesRealOrderingSnapshotsAndMapsQuantityCostsOptionsAndCancellationThroughGateway()
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
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var optionId = Guid.NewGuid();
        await using (var db = new ReportSalesDb(billingConnection, tenant))
        {
            await db.Database.EnsureCreatedAsync();
            SeedDishReportPayment(db, restaurant.RestaurantId, orderId, lineId, itemId, optionId);
            await db.SaveChangesAsync();
        }

        await using (var db = new ReportOrderDb(orderingConnection, tenant))
        {
            await db.Database.EnsureCreatedAsync();
            var order = ReportOrder(orderId, restaurant.RestaurantId, "DineIn");
            var line = new OrderItem
            {
                OrderItemId = lineId,
                ItemRef = itemId,
                NameSnapshot = "Bún bò",
                UnitPriceSnapshot = 80,
                Quantity = 3,
                CancelledQuantity = 1,
                Status = "Pending"
            };
            line.OrderItemOptions.Add(new OrderItemOption
            {
                Id = Guid.NewGuid(),
                OptionRef = optionId,
                NameSnapshot = "Thêm bò",
                Quantity = 1,
                ExtraPriceSnapshot = 20
            });
            order.OrderItems.Add(line);
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        }

        await using var ordering = ReportOrderingHost(orderingConnection);
        await using var billing = ReportBillingWithOrdering(billingConnection, ordering);
        await using var gateway = ReportGatewayHost(billing);
        using var client = gateway.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "manager");
        var path = ReportUrl(restaurant.RestaurantId).Replace("/reports/period", "/reports/dishes");
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = json.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("quantityComplete").GetBoolean(), result.ToString());
        Assert.Equal("VND", result.GetProperty("currency").GetString());
        using var emptyItem = await client.GetAsync(path + "&itemId=");
        Assert.Equal(HttpStatusCode.OK, emptyItem.StatusCode);
        using var emptyItemJson = JsonDocument.Parse(await emptyItem.Content.ReadAsStringAsync());
        Assert.Equal(result.GetProperty("items").GetRawText(), emptyItemJson.RootElement.GetProperty("result").GetProperty("items").GetRawText());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path + "&itemId=" + Guid.Empty)).StatusCode);
        using var channelReport = await client.GetAsync(path + "&channel=DineIn");
        Assert.Equal(HttpStatusCode.OK, channelReport.StatusCode);
        using var channelJson = JsonDocument.Parse(await channelReport.Content.ReadAsStringAsync());
        Assert.Equal("VND", channelJson.RootElement.GetProperty("result").GetProperty("currency").GetString());
        Assert.Contains(channelJson.RootElement.GetProperty("result").GetProperty("warnings").EnumerateArray(),
            value => value.GetProperty("code").GetString() == "ExclusiveChannelFilter");
        var item = Assert.Single(result.GetProperty("items").EnumerateArray());
        Assert.Equal(2m, item.GetProperty("quantity").GetProperty("quantity").GetDecimal());
        Assert.Equal(200m, item.GetProperty("grossSales").GetProperty("amount").GetDecimal());
        Assert.Equal(50m, item.GetProperty("cost").GetProperty("amount").GetDecimal());
        Assert.Equal(150m, item.GetProperty("grossProfit").GetProperty("amount").GetDecimal());
        Assert.Equal("Bún bò", item.GetProperty("names")[0].GetString());
        Assert.Equal(100m, item.GetProperty("prices")[0].GetProperty("unitPrice").GetDecimal());
        Assert.Single(item.GetProperty("prices")[0].GetProperty("options").EnumerateArray());
        using var unmatched = await client.GetAsync(path + "&itemId=" + Guid.NewGuid());
        using var unmatchedJson = JsonDocument.Parse(await unmatched.Content.ReadAsStringAsync());
        Assert.Empty(unmatchedJson.RootElement.GetProperty("result").GetProperty("items").EnumerateArray());
        Assert.DoesNotContain(unmatchedJson.RootElement.GetProperty("result").GetProperty("warnings").EnumerateArray(),
            value => value.GetProperty("code").GetString() == "DishContributionBeforeRefunds");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path + "&itemId=bad")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "cashier");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "manager");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path.Replace(restaurant.RestaurantId.ToString(), other.RestaurantId.ToString()))).StatusCode);

        await AssignReportManager(other.RestaurantId);
        client.DefaultRequestHeaders.Add("X-Restaurant-Id", other.RestaurantId.ToString());
        using var mismatched = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, mismatched.StatusCode);
        client.DefaultRequestHeaders.Remove("X-Restaurant-Id");
        client.DefaultRequestHeaders.Add("X-Restaurant-Id", restaurant.RestaurantId.ToString());
        using var matched = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, matched.StatusCode);

        using var billingChannel = GrpcChannel.ForAddress(billing.Server.BaseAddress, new GrpcChannelOptions
        {
            HttpHandler = billing.Server.CreateHandler()
        });
        var reports = new Backend.Contracts.Billing.RestaurantReportGrpc.RestaurantReportGrpcClient(billingChannel);
        var inconsistent = new Backend.Contracts.Billing.DishReportRequest
        {
            RestaurantId = restaurant.RestaurantId.ToString(),
            Query = new Backend.Contracts.Billing.ReportPeriodRequest
            {
                RestaurantId = other.RestaurantId.ToString(),
                From = "2026-11-25T00:00:00Z",
                To = "2026-11-26T00:00:00Z",
                GroupBy = "day"
            }
        };
        var invalid = await Assert.ThrowsAsync<RpcException>(async () => await reports.GetDishesAsync(inconsistent,
            new Metadata
            {
                { "authorization", "Bearer manager" }
            }));
        Assert.Equal(StatusCode.InvalidArgument, invalid.StatusCode);

        using var channel = GrpcChannel.ForAddress(ordering.Server.BaseAddress, new GrpcChannelOptions
        {
            HttpHandler = ordering.Server.CreateHandler()
        });
        var rpc = new billingapi::Backend.Contracts.Ordering.BillingOrderGrpc.BillingOrderGrpcClient(channel);
        var sourceRequest = new billingapi::Backend.Contracts.Ordering.OrderReportChannelsRequest
        {
            RestaurantId = restaurant.RestaurantId.ToString()
        };
        sourceRequest.OrderIds.Add(orderId.ToString());
        var denied = await Assert.ThrowsAsync<RpcException>(async () => await rpc.GetReportItemsAsync(sourceRequest,
            new Metadata
            {
                { "authorization", "Bearer cashier" }
            }));
        Assert.Equal(StatusCode.PermissionDenied, denied.StatusCode);

        await using (var db = new ReportOrderDb(orderingConnection))
        {
            await db.OrderItems.Where(value => value.OrderItemId == lineId)
                .ExecuteUpdateAsync(update => update.SetProperty(value => value.CancelledQuantity, 2));
        }

        using var partial = await client.GetAsync(path);
        using var partialJson = JsonDocument.Parse(await partial.Content.ReadAsStringAsync());
        var partialItem = Assert.Single(partialJson.RootElement.GetProperty("result").GetProperty("items").EnumerateArray());
        Assert.Equal(1m, partialItem.GetProperty("quantity").GetProperty("quantity").GetDecimal());
        Assert.Equal(JsonValueKind.Null, partialItem.GetProperty("cost").GetProperty("amount").ValueKind);
        await using (var db = new ReportOrderDb(orderingConnection))
        {
            await db.OrderItems.Where(value => value.OrderItemId == lineId)
                .ExecuteUpdateAsync(update => update.SetProperty(value => value.CancelledQuantity, 3));
        }

        using var cancelled = await client.GetAsync(path);
        using var cancelledJson = JsonDocument.Parse(await cancelled.Content.ReadAsStringAsync());
        Assert.Empty(cancelledJson.RootElement.GetProperty("result").GetProperty("items").EnumerateArray());
        Assert.DoesNotContain(cancelledJson.RootElement.GetProperty("result").GetProperty("warnings").EnumerateArray(),
            value => value.GetProperty("code").GetString() == "DishCostUnavailable");
        using var sales = await client.GetAsync(path.Replace("/reports/dishes", "/reports/sales"));
        using var salesJson = JsonDocument.Parse(await sales.Content.ReadAsStringAsync());
        Assert.Equal(216m, salesJson.RootElement.GetProperty("result").GetProperty("totals").GetProperty("collections").GetProperty("amount").GetDecimal());
        await using var verify = new ReportSalesDb(billingConnection, tenant);
        Assert.Equal("Paid", (await verify.Payments.SingleAsync()).Status);
        Assert.Equal(2m, (await verify.BillLines.SingleAsync(value => value.Quantity > 0)).Quantity);
    }

    [Fact]
    public async Task R5DiscountAndMissingCostCrossTheGatewayWithoutInventingNetProfit()
    {
        var restaurant = await CreateRestaurant();
        await AssignReportManager(restaurant.RestaurantId);
        await using var billingConnection = new SqliteConnection("Data Source=:memory:");
        await using var orderingConnection = new SqliteConnection("Data Source=:memory:");
        await billingConnection.OpenAsync();
        await orderingConnection.OpenAsync();
        var tenant = new RestaurantDataScope();
        tenant.Select(restaurant.RestaurantId);
        var orderId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var optionId = Guid.NewGuid();
        await using (var db = new ReportSalesDb(billingConnection, tenant))
        {
            await db.Database.EnsureCreatedAsync();
            SeedDishReportPayment(db, restaurant.RestaurantId, orderId, lineId, itemId, optionId);
            var payment = db.ChangeTracker.Entries<Payment>().Single().Entity;
            payment.Bill.DiscountAmount = 20;
            payment.Bill.TotalAmount = 196;
            payment.Amount = 196;
            payment.Bill.BillLines.Single(value => value.Quantity > 0).DiscountAllocated = 20;
            await db.SaveChangesAsync();
        }

        await using (var db = new ReportOrderDb(orderingConnection, tenant))
        {
            await db.Database.EnsureCreatedAsync();
            var order = ReportOrder(orderId, restaurant.RestaurantId, "DineIn");
            order.OrderItems.Add(new OrderItem
            {
                OrderItemId = lineId,
                ItemRef = itemId,
                NameSnapshot = "Bún bò",
                UnitPriceSnapshot = 100,
                Quantity = 2,
                CancelledQuantity = 0,
                Status = "Pending"
            });
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        }

        await using var ordering = ReportOrderingHost(orderingConnection);
        await using var billing = ReportBillingWithOrdering(billingConnection, ordering);
        await using var gateway = ReportGatewayHost(billing);
        using var client = gateway.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "manager");
        var path = ReportUrl(restaurant.RestaurantId).Replace("/reports/period", "/reports/dishes");
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = json.RootElement.GetProperty("result");
        var item = Assert.Single(result.GetProperty("items").EnumerateArray());
        Assert.Equal(20m, item.GetProperty("discounts").GetProperty("amount").GetDecimal());
        Assert.Equal(180m, item.GetProperty("netSales").GetProperty("amount").GetDecimal());
        Assert.Equal(130m, item.GetProperty("grossProfit").GetProperty("amount").GetDecimal());
        Assert.Equal(130m / 180m * 100m, item.GetProperty("contributionMarginPercent").GetDecimal());
        Assert.Contains(result.GetProperty("warnings").EnumerateArray(),
            value => value.GetProperty("code").GetString() == "DishContributionBeforeRefunds");
        using var sales = await client.GetAsync(path.Replace("/dishes", "/sales"));
        Assert.Equal(HttpStatusCode.OK, sales.StatusCode);
        using var salesJson = JsonDocument.Parse(await sales.Content.ReadAsStringAsync());
        var totals = salesJson.RootElement.GetProperty("result").GetProperty("totals");
        Assert.Equal(196m, totals.GetProperty("collections").GetProperty("amount").GetDecimal());
        Assert.Equal(20m, totals.GetProperty("discounts").GetProperty("amount").GetDecimal());
        Assert.Equal(180m, totals.GetProperty("dishRevenue").GetProperty("amount").GetDecimal());
        Assert.Equal(JsonValueKind.Null, totals.GetProperty("netDishRevenue").GetProperty("amount").ValueKind);
        await using (var db = new ReportSalesDb(billingConnection, tenant))
        {
            await db.BillLines.Where(value => value.OrderItemRef == lineId)
                .ExecuteUpdateAsync(update => update.SetProperty(value => value.CogsSnapshot, (decimal?)null));
        }

        using var missing = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, missing.StatusCode);
        using var missingJson = JsonDocument.Parse(await missing.Content.ReadAsStringAsync());
        var incomplete = Assert.Single(missingJson.RootElement.GetProperty("result").GetProperty("items").EnumerateArray());
        Assert.Equal(180m, incomplete.GetProperty("netSales").GetProperty("amount").GetDecimal());
        Assert.Equal(JsonValueKind.Null, incomplete.GetProperty("grossProfit").GetProperty("amount").ValueKind);
        Assert.Equal(JsonValueKind.Null, incomplete.GetProperty("contributionMarginPercent").ValueKind);
    }

    private static void SeedDishReportPayment(BillingDb db, Guid restaurant, Guid order, Guid lineId, Guid item, Guid option)
    {
        var at = new DateTime(2026, 11, 25, 2, 0, 0, DateTimeKind.Utc);
        var bill = new Bill
        {
            BillId = Guid.NewGuid(),
            RestaurantId = restaurant,
            BillNumber = "DISH-FIXTURE",
            Status = "Paid",
            Currency = "VND",
            PaidAt = at,
            SubTotal = 200,
            VatAmount = 16,
            TotalAmount = 216
        };
        bill.BillLines.Add(new BillLine
        {
            BillLineId = Guid.NewGuid(),
            OrderItemRef = lineId,
            TableSessionRef = Guid.NewGuid(),
            OriginalTableRef = Guid.NewGuid(),
            Quantity = 2,
            UnitPriceSnapshot = 100,
            VatAllocated = 16,
            CogsSnapshot = 50,
            ItemSnapshot = JsonSerializer.Serialize(new Billing.Common.DTOs.BillItemSnapshot
            {
                OrderId = order,
                ItemId = item,
                Name = "Bún bò",
                BaseUnitPrice = 80,
                Options = [new Billing.Common.DTOs.BillOptionSnapshot
                {
                    OptionId = option,
                    Name = "Thêm bò",
                    Quantity = 1,
                    ExtraPrice = 20
                }]
            })
        });
        bill.BillLines.Add(new BillLine
        {
            BillLineId = Guid.NewGuid(),
            OrderItemRef = Guid.NewGuid(),
            TableSessionRef = Guid.NewGuid(),
            OriginalTableRef = Guid.NewGuid(),
            Quantity = 0,
            ItemSnapshot = "invalid-json"
        });
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
                    RestaurantId = restaurant,
                    Code = "MAIN"
                },
                CashierUserRef = Guid.NewGuid(),
                OpenedAt = at.AddHours(-1),
                Status = "Active"
            },
            MethodCode = "BankTransfer",
            Status = "Paid",
            Amount = 216,
            PaidAt = at,
            VerificationSource = "PayOSQuery",
            IdempotencyKey = "PayOS:DISH",
            ProviderOrderCode = "1",
            ProviderPaymentLinkId = "fixture",
            ProviderTransactionId = "fixture-txn"
        });
    }
}
