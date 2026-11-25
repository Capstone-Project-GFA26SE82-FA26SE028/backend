using System.Text.Json;
using Billing.BLL.Implements;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.Common.Settings;
using Billing.DAL.DTOs;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.Extensions.Options;

namespace Billing.Tests;

public sealed class RestaurantDishReportServiceTests
{
    [Fact]
    public async Task UsesRemainingQuantityWithoutSubtractingCancellationTwiceOrMultiplyingTotalLineCost()
    {
        var fixture = new Fixture();
        var result = await fixture.Get();
        var item = Assert.Single(result.Items);
        Assert.True(result.QuantityComplete);
        Assert.Equal(2m, item.Quantity.Quantity);
        Assert.Equal(200m, item.GrossSales.Amount);
        Assert.Equal(200m, item.NetSales.Amount);
        Assert.Equal(50m, item.Cost.Amount);
        Assert.Equal(150m, item.GrossProfit.Amount);
        Assert.Equal(100m, Assert.Single(item.Prices).UnitPrice);
        Assert.Single(item.Prices.Single().Options);
        Assert.Equal(216m, fixture.Payment.Amount);
        Assert.Equal(2m, fixture.Line.Quantity);
    }

    [Fact]
    public async Task FurtherCancellationDoesNotInventCostOrDiscountAllocation()
    {
        var fixture = new Fixture();
        fixture.SetOrder(cancelled: 2);
        fixture.Line.DiscountAllocated = 10;
        fixture.Payment.Bill.DiscountAmount = 10;
        fixture.Payment.Bill.TotalAmount = 206;
        fixture.Payment.Amount = 206;
        var item = Assert.Single((await fixture.Get()).Items);
        Assert.Equal(1m, item.Quantity.Quantity);
        Assert.Equal(100m, item.GrossSales.Amount);
        Assert.Null(item.NetSales.Amount);
        Assert.Null(item.Cost.Amount);
        Assert.Null(item.GrossProfit.Amount);
        Assert.Equal(50m, fixture.Line.CogsSnapshot);
    }

    [Theory]
    [InlineData("Cancelled")]
    [InlineData("Rejected")]
    [InlineData("PendingConfirmation")]
    public async Task ExcludesOrdersThatAreNotConfirmedSalesWithoutChangingPaidBill(string status)
    {
        var fixture = new Fixture();
        fixture.SetOrder(status: status);
        var result = await fixture.Get();
        Assert.Empty(result.Items);
        Assert.True(result.QuantityComplete);
        Assert.Equal("Paid", fixture.Payment.Status);
        Assert.Equal(216m, fixture.Payment.Bill.TotalAmount);
    }

    [Fact]
    public async Task FullyCancelledAndZeroQuantityLinesDoNotRequireCosts()
    {
        var fixture = new Fixture();
        fixture.SetOrder(cancelled: 3);
        fixture.Line.CogsSnapshot = null;
        fixture.Payment.Bill.BillLines.Add(new BillLine
        {
            Quantity = 0,
            CogsSnapshot = null,
            ItemSnapshot = "invalid-json"
        });
        var result = await fixture.Get();
        Assert.Empty(result.Items);
        Assert.DoesNotContain(result.Warnings, value => value.Code == "DishCostUnavailable");
    }

    [Fact]
    public async Task MissingCostIsNullAndNeverUsesSellingPriceAsCost()
    {
        var fixture = new Fixture();
        fixture.Line.CogsSnapshot = null;
        var item = Assert.Single((await fixture.Get()).Items);
        Assert.Equal(0m, item.Cost.KnownAmount);
        Assert.Null(item.Cost.Amount);
        Assert.Null(item.GrossProfit.Amount);
        Assert.Equal(200m, item.GrossSales.Amount);
    }

    [Fact]
    public async Task MissingOrderKeepsKnownSnapshotButDoesNotInventAQuantity()
    {
        var fixture = new Fixture();
        fixture.Orders.Clear();
        var result = await fixture.Get();
        Assert.False(result.QuantityComplete);
        var item = Assert.Single(result.Items);
        Assert.Equal(0m, item.Quantity.KnownQuantity);
        Assert.Null(item.Quantity.Quantity);
        Assert.Null(item.GrossSales.Amount);
        Assert.Null(item.Cost.Amount);
    }

    [Fact]
    public async Task PriceAndNameComeFromBillSnapshotAndOptionsAreNotAddedTwice()
    {
        var fixture = new Fixture();
        var current = fixture.Orders[fixture.OrderId];
        fixture.Orders[fixture.OrderId] = current with
        {
            Items =
            [
                current.Items.Single() with
                {
                    UnitPrice = 999999,
                    Name = "Tên khác ở nguồn"
                }
            ]
        };
        var item = Assert.Single((await fixture.Get()).Items);
        Assert.Equal("Bún bò", Assert.Single(item.Names));
        Assert.Equal(100m, Assert.Single(item.Prices).UnitPrice);
        Assert.Equal(200m, item.GrossSales.Amount);
    }

    [Fact]
    public async Task ItemFilterOnlyFiltersDishRowsAndRejectsEmptyGuid()
    {
        var fixture = new Fixture();
        Assert.Empty((await fixture.Get(Guid.NewGuid())).Items);
        Assert.Single((await fixture.Get(fixture.ItemId)).Items);
        await Assert.ThrowsAsync<BillingException>(() => fixture.Get(Guid.Empty));
        Assert.Equal(216m, fixture.Payment.Amount);
    }

    [Fact]
    public async Task DuplicateOrderItemIsExcludedInsteadOfCountedTwice()
    {
        var fixture = new Fixture();
        fixture.Payment.Bill.BillLines.Add(new BillLine
        {
            OrderItemRef = fixture.Line.OrderItemRef,
            Quantity = 2,
            UnitPriceSnapshot = 100,
            ItemSnapshot = fixture.Line.ItemSnapshot
        });
        var result = await fixture.Get();
        Assert.False(result.QuantityComplete);
        Assert.Empty(result.Items);
        Assert.Contains(result.Warnings, value => value.Code == "DuplicateDishSource");
    }

    [Fact]
    public async Task TakeawayUsesSnapshotPriceAndOptionsOnceWhileFinancialGapsRemainNull()
    {
        var fixture = new Fixture();
        fixture.Repository.Payments.Clear();
        var online = RestaurantSalesReportServiceTests.Online(fixture.RestaurantId, RestaurantSalesReportServiceTests.At(3));
        online.OrderId = fixture.OrderId;
        online.Amount = 300;
        fixture.Repository.Online.Add(online);
        fixture.SetOrder(status: "Accepted", channel: "Takeaway");
        var item = Assert.Single((await fixture.Get()).Items);
        Assert.Equal(2m, item.Quantity.Quantity);
        Assert.Equal(200m, item.GrossSales.Amount);
        Assert.Null(item.NetSales.Amount);
        Assert.Null(item.Cost.Amount);
        Assert.Null(item.GrossProfit.Amount);
    }

    [Fact]
    public async Task AccessFailurePreventsSourceReads()
    {
        var fixture = new Fixture();
        fixture.Denied = true;
        await Assert.ThrowsAsync<BillingException>(() => fixture.Get());
        Assert.Equal(0, fixture.Repository.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RanksByQuantityAndKeepsHistoricalPriceVariants(bool sameItem)
    {
        var fixture = new Fixture();
        var payment = RestaurantSalesReportServiceTests.PaidBill(fixture.RestaurantId, RestaurantSalesReportServiceTests.At(3), 50, 0, 4);
        var line = payment.Bill.BillLines.Single();
        var itemId = sameItem ? fixture.ItemId : Guid.NewGuid();
        var orderId = Guid.NewGuid();
        line.OrderItemRef = Guid.NewGuid();
        line.Quantity = 5;
        line.UnitPriceSnapshot = 10;
        line.CogsSnapshot = 10;
        line.ItemSnapshot = JsonSerializer.Serialize(new BillItemSnapshot
        {
            OrderId = orderId,
            ItemId = itemId,
            Name = "Tên snapshot khác",
            BaseUnitPrice = 10
        });
        fixture.Orders[orderId] = new ReportOrderSnapshotDTO(orderId, "DineIn", "Served",
            [new ReportOrderItemDTO(line.OrderItemRef, itemId, "Tên snapshot khác", 10, 5, 0, "Served", [])]);
        fixture.Repository.Payments.Add(payment);
        var result = await fixture.Get();
        Assert.Equal(itemId, result.Items[0].ItemId);
        Assert.Equal(sameItem ? 7m : 5m, result.Items[0].Quantity.Quantity);
        if (sameItem)
        {
            var item = Assert.Single(result.Items);
            Assert.Equal(2, item.Prices.Count);
            Assert.Equal(2, item.Names.Count);
            Assert.Equal(60m, item.Cost.Amount);
            Assert.Equal(190m, item.GrossProfit.Amount);
        }
        else
        {
            Assert.Equal(2, result.Items.Count);
        }
    }

    [Fact]
    public async Task MixedChannelFiltersTheWholeBillBeforeItemFiltering()
    {
        var fixture = new Fixture();
        var orderId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        fixture.Payment.Bill.BillLines.Add(new BillLine
        {
            OrderItemRef = lineId,
            Quantity = 1,
            UnitPriceSnapshot = 50,
            CogsSnapshot = 25,
            VatAllocated = 4,
            ItemSnapshot = JsonSerializer.Serialize(new BillItemSnapshot
            {
                OrderId = orderId,
                ItemId = id,
                Name = "Mang về",
                BaseUnitPrice = 50
            })
        });
        fixture.Orders[orderId] = new ReportOrderSnapshotDTO(orderId, "Takeaway", "Confirmed",
            [new ReportOrderItemDTO(lineId, id, "Mang về", 50, 1, 0, "Pending", [])]);
        fixture.Payment.Bill.SubTotal = 250;
        fixture.Payment.Bill.VatAmount = 20;
        fixture.Payment.Bill.TotalAmount = 270;
        fixture.Payment.Amount = 270;
        Assert.Empty((await fixture.Get(channel: "DineIn")).Items);
        Assert.Equal(2, (await fixture.Get(channel: "Mixed")).Items.Count);
        Assert.Single((await fixture.Get(fixture.ItemId, "Mixed")).Items);
    }

    [Fact]
    public async Task InvalidBillHeaderDoesNotProduceCompleteNetSalesOrProfit()
    {
        var fixture = new Fixture();
        fixture.Payment.Bill.SubTotal = 999;
        var item = Assert.Single((await fixture.Get()).Items);
        Assert.Equal(2m, item.Quantity.Quantity);
        Assert.Equal(200m, item.GrossSales.Amount);
        Assert.Null(item.NetSales.Amount);
        Assert.Null(item.GrossProfit.Amount);
    }

    [Fact]
    public async Task TakeawaySnapshotThatDoesNotMatchOriginalQuoteIsNotCountedAsCompleteSales()
    {
        var fixture = new Fixture();
        fixture.Repository.Payments.Clear();
        var online = RestaurantSalesReportServiceTests.Online(fixture.RestaurantId, RestaurantSalesReportServiceTests.At(3));
        online.OrderId = fixture.OrderId;
        online.Amount = 50;
        fixture.Repository.Online.Add(online);
        fixture.SetOrder(status: "Accepted", channel: "Takeaway");
        var result = await fixture.Get();
        Assert.False(result.QuantityComplete);
        Assert.Empty(result.Items);
        Assert.Contains(result.Warnings, value => value.Code == "TakeawaySnapshotMismatch");
    }

    [Fact]
    public async Task KnownOtherItemGapDoesNotChangeAHealthyItemsMetricsBetweenAllAndFiltered()
    {
        var fixture = new Fixture();
        var other = Guid.NewGuid();
        fixture.AddBill(other, 30, 10, false);
        var all = await fixture.Get();
        var filtered = await fixture.Get(fixture.ItemId);
        var healthy = all.Items.Single(value => value.ItemId == fixture.ItemId);
        var selected = Assert.Single(filtered.Items);
        Assert.False(all.QuantityComplete);
        Assert.Null(all.Items.Single(value => value.ItemId == other).Quantity.Quantity);
        Assert.Equal(2m, healthy.Quantity.Quantity);
        Assert.Equal(200m, healthy.GrossSales.Amount);
        Assert.Equal(50m, healthy.Cost.Amount);
        Assert.Equal(150m, healthy.GrossProfit.Amount);
        Assert.Equal(JsonSerializer.Serialize(selected), JsonSerializer.Serialize(healthy));
    }

    [Theory]
    [InlineData("bad-price")]
    [InlineData("bad-quantity")]
    [InlineData("duplicate")]
    public async Task IdentifiableOtherItemErrorsDoNotInvalidateHealthyItems(string issue)
    {
        var fixture = new Fixture();
        var other = fixture.AddBill(Guid.NewGuid(), 30, 10);
        var line = other.Bill.BillLines.Single();
        var snapshot = JsonSerializer.Deserialize<BillItemSnapshot>(line.ItemSnapshot)!;
        if (issue == "bad-price")
        {
            line.UnitPriceSnapshot = 31;
        }
        else if (issue == "bad-quantity")
        {
            var order = fixture.Orders[snapshot.OrderId];
            fixture.Orders[snapshot.OrderId] = order with
            {
                Items =
                [
                    order.Items.Single() with
                    {
                        CancelledQuantity = 2
                    }
                ]
            };
        }
        else
        {
            other.Bill.BillLines.Add(new BillLine
            {
                OrderItemRef = line.OrderItemRef,
                Quantity = 1,
                UnitPriceSnapshot = 30,
                ItemSnapshot = line.ItemSnapshot
            });
        }

        var all = await fixture.Get();
        var selected = await fixture.Get(fixture.ItemId);
        Assert.False(all.QuantityComplete);
        Assert.True(selected.QuantityComplete);
        Assert.Equal(JsonSerializer.Serialize(Assert.Single(selected.Items)),
            JsonSerializer.Serialize(all.Items.Single(value => value.ItemId == fixture.ItemId)));
    }

    [Theory]
    [InlineData("missing-online-order")]
    [InlineData("invalid-bill-json")]
    public async Task UnattributableGapsStillMakeEveryPotentiallyAffectedItemIncomplete(string issue)
    {
        var fixture = new Fixture();
        if (issue == "missing-online-order")
        {
            fixture.Repository.Online.Add(RestaurantSalesReportServiceTests.Online(fixture.RestaurantId,
                RestaurantSalesReportServiceTests.At(3)));
        }
        else
        {
            fixture.AddBill(Guid.NewGuid(), 30, 10).Bill.BillLines.Single().ItemSnapshot = "invalid-json";
        }

        var all = await fixture.Get();
        var selected = await fixture.Get(fixture.ItemId);
        Assert.False(all.QuantityComplete);
        Assert.False(selected.QuantityComplete);
        Assert.Null(all.Items.Single(value => value.ItemId == fixture.ItemId).Quantity.Quantity);
        Assert.Null(Assert.Single(selected.Items).Quantity.Quantity);
    }

    [Fact]
    public async Task KnownProfitOnlyUsesRowsWithBothKnownNetSalesAndCost()
    {
        var fixture = new Fixture();
        fixture.Line.CogsSnapshot = 60;
        fixture.AddBill(fixture.ItemId, 100, null);
        var missingNet = fixture.AddBill(fixture.ItemId, 100, 40);
        missingNet.Bill.SubTotal = 999;
        var item = Assert.Single((await fixture.Get(fixture.ItemId)).Items);
        Assert.Equal(300m, item.NetSales.KnownAmount);
        Assert.Null(item.NetSales.Amount);
        Assert.Equal(100m, item.Cost.KnownAmount);
        Assert.Null(item.Cost.Amount);
        Assert.Equal(140m, item.GrossProfit.KnownAmount);
        Assert.Null(item.GrossProfit.Amount);
    }

    [Fact]
    public async Task DishCurrencyAndExclusiveChannelWarningMatchTheSalesContract()
    {
        var fixture = new Fixture();
        var all = await fixture.Get();
        var selected = await fixture.Get(channel: "DineIn");
        Assert.Equal("VND", all.Currency);
        Assert.Equal("VND", selected.Currency);
        Assert.DoesNotContain(all.Warnings, value => value.Code == "ExclusiveChannelFilter");
        Assert.Contains(selected.Warnings, value => value.Code == "ExclusiveChannelFilter");
    }

    private sealed class Fixture : IRestaurantReportContextClient, IRestaurantReportItemsClient
    {
        public Guid RestaurantId { get; } = Guid.NewGuid();
        public Guid ItemId { get; } = Guid.NewGuid();
        public Guid OrderId { get; } = Guid.NewGuid();
        public Repository Repository { get; } = new();
        public Dictionary<Guid, ReportOrderSnapshotDTO> Orders { get; } = [];
        public Payment Payment { get; }
        public BillLine Line => Payment.Bill.BillLines.First();
        public bool Denied { get; set; }
        private readonly BillOptionSnapshot _option = new()
        {
            OptionId = Guid.NewGuid(),
            Name = "Thêm bò",
            Quantity = 1,
            ExtraPrice = 20
        };

        public Fixture()
        {
            Payment = RestaurantSalesReportServiceTests.PaidBill(RestaurantId, RestaurantSalesReportServiceTests.At(2), 200, 0, 16);
            Line.OrderItemRef = Guid.NewGuid();
            Line.Quantity = 2;
            Line.UnitPriceSnapshot = 100;
            Line.CogsSnapshot = 50;
            Line.ItemSnapshot = JsonSerializer.Serialize(new BillItemSnapshot
            {
                OrderId = OrderId,
                ItemId = ItemId,
                Name = "Bún bò",
                BaseUnitPrice = 80,
                Options = [_option]
            });
            Repository.Payments.Add(Payment);
            SetOrder();
        }

        public Payment AddBill(Guid itemId, decimal price, decimal? cost, bool hasItem = true)
        {
            var payment = RestaurantSalesReportServiceTests.PaidBill(RestaurantId, RestaurantSalesReportServiceTests.At(3), price, 0, 0);
            var line = payment.Bill.BillLines.Single();
            var orderId = Guid.NewGuid();
            line.OrderItemRef = Guid.NewGuid();
            line.CogsSnapshot = cost;
            line.ItemSnapshot = JsonSerializer.Serialize(new BillItemSnapshot
            {
                OrderId = orderId,
                ItemId = itemId,
                Name = "Snapshot bổ sung",
                BaseUnitPrice = price
            });
            Orders[orderId] = new ReportOrderSnapshotDTO(orderId, "DineIn", "Confirmed", hasItem
                ? [new ReportOrderItemDTO(line.OrderItemRef, itemId, "Snapshot bổ sung", price, 1, 0, "Pending", [])]
                : []);
            Repository.Payments.Add(payment);
            return payment;
        }

        public void SetOrder(int cancelled = 1, string status = "Confirmed", string channel = "DineIn")
        {
            Orders[OrderId] = new ReportOrderSnapshotDTO(OrderId, channel, status,
                [new ReportOrderItemDTO(Line.OrderItemRef, ItemId, "Bún bò", 80, 3, cancelled, "Pending", [_option])]);
        }

        public Task<RestaurantReportContextDTO> GetAsync(Guid restaurantId, CancellationToken ct)
        {
            if (Denied)
            {
                throw new BillingException(403, "Denied");
            }

            return Task.FromResult(new RestaurantReportContextDTO(RestaurantId, "Asia/Ho_Chi_Minh"));
        }

        public Task<IReadOnlyDictionary<Guid, ReportOrderSnapshotDTO>> GetItemsAsync(Guid restaurantId, Guid[] orderIds, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyDictionary<Guid, ReportOrderSnapshotDTO>>(Orders);
        }

        public Task<RestaurantDishReportDTO> Get(Guid? itemId = null, string channel = "all")
        {
            var service = new RestaurantDishReportService(new RestaurantReportService(this), Repository, this,
                Options.Create(new CashPaymentSettings
                {
                    MethodCode = "Cash"
                }));
            return service.GetDishes(RestaurantId, new ReportPeriodQueryDTO
            {
                From = "2026-11-25T00:00:00Z",
                To = "2026-11-26T00:00:00Z"
            }, new SalesReportFilterDTO
            {
                Channel = channel
            }, itemId, default);
        }
    }

    private sealed class Repository : IRestaurantReportRepository
    {
        public List<Payment> Payments { get; } = [];
        public List<OnlinePayment> Online { get; } = [];
        public int Calls { get; private set; }

        public Task<RestaurantReportSource> GetSourceAsync(Guid restaurantId, DateTime fromUtc, DateTime toUtc,
            CancellationToken ct, SalesReportFilterDTO? filters = null)
        {
            Calls++;
            return Task.FromResult(new RestaurantReportSource(Payments, Online, [], 0, 0, 0));
        }
    }
}
