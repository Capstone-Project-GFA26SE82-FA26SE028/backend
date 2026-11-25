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

public sealed class RestaurantSalesReportServiceTests
{
    [Fact]
    public async Task BillLinesDoNotMultiplyCollectionsAndDiscountsVatFeesRemainSeparate()
    {
        var fixture = new Fixture();
        var payment = PaidBill(fixture.RestaurantId, At(2), 150, 10, 11.76m, 7);
        var first = payment.Bill.BillLines.Single();
        first.Quantity = 1;
        first.UnitPriceSnapshot = 50;
        first.DiscountAllocated = 4;
        first.VatAllocated = 4;
        first.ServiceAllocated = 2;
        payment.Bill.BillLines.Add(new BillLine
        {
            BillLineId = Guid.NewGuid(),
            Quantity = 2,
            UnitPriceSnapshot = 50,
            DiscountAllocated = 6,
            VatAllocated = 7.76m,
            ServiceAllocated = 5,
            ItemSnapshot = first.ItemSnapshot
        });
        fixture.Repository.Payments.Add(payment);
        var result = await fixture.Get();
        Assert.Equal(159m, result.Totals.Collections.Amount);
        Assert.Equal(140m, result.Totals.DishRevenue.Amount);
        Assert.Equal(10m, result.Totals.Discounts.Amount);
        Assert.Equal(11.76m, result.Totals.Vat.Amount);
        Assert.Equal(7m, result.Totals.ServiceFees.Amount);
        Assert.Equal(0.24m, result.Totals.RoundingAdjustment.Amount);
        Assert.Equal(1, result.Totals.PaidOrders.Count);
        Assert.Null(result.Totals.Refunds.Amount);
        Assert.Null(result.Totals.NetCollections.Amount);
        Assert.Contains(result.Warnings, value => value.Code == "DineInRefundsUnavailable");
    }

    [Fact]
    public async Task RefundedTakeawayKeepsItsSaleInTheOriginalPeriodAndRefundInVerificationPeriod()
    {
        var fixture = new Fixture();
        var payment = Online(fixture.RestaurantId, At(3));
        payment.Status = "Refunded";
        payment.RefundedAt = At(3).AddDays(1);
        payment.RefundVerifiedBy = Guid.NewGuid();
        payment.RefundReference = "refund-reference";
        payment.RefundEvidence = "https://example.test/refund";
        fixture.Repository.Online.Add(payment);
        var sale = await fixture.Get();
        Assert.Equal(50m, sale.Totals.Collections.Amount);
        Assert.Equal(0m, sale.Totals.Refunds.KnownAmount);
        Assert.Equal(50m, sale.Totals.NetCollections.KnownAmount);
        Assert.Null(sale.Totals.DishRevenue.Amount);
        Assert.Null(sale.Totals.Vat.Amount);
        Assert.Equal(1, sale.Totals.PaidOrders.Count);
        var refund = await fixture.Get("day", "2026-11-26T00:00:00+07:00", "2026-11-27T00:00:00+07:00");
        Assert.Equal(0m, refund.Totals.Collections.Amount);
        Assert.Equal(50m, refund.Totals.Refunds.KnownAmount);
        Assert.Equal(-50m, refund.Totals.NetCollections.KnownAmount);
        Assert.Null(refund.Totals.NetCollections.Amount);
        Assert.Equal(0, refund.Totals.PaidOrders.Count);
        Assert.Equal(0m, refund.Totals.DishRevenue.Amount);
    }

    [Fact]
    public async Task PendingRefundDoesNotReduceVerifiedCollections()
    {
        var fixture = new Fixture();
        var payment = Online(fixture.RestaurantId, At(3));
        payment.Status = "RefundPending";
        fixture.Repository.Online.Add(payment);
        var result = await fixture.Get();
        Assert.Equal(50m, result.Totals.Collections.Amount);
        Assert.Equal(0m, result.Totals.Refunds.KnownAmount);
        Assert.Equal(50m, result.Totals.NetCollections.KnownAmount);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    public async Task UnpaidOnlineAttemptsDoNotIncreaseCollections(string status)
    {
        var fixture = new Fixture();
        var payment = Online(fixture.RestaurantId, At(3));
        payment.Status = status;
        payment.PaidAt = null;
        fixture.Repository.Online.Add(payment);
        var result = await fixture.Get();
        Assert.Equal(0m, result.Totals.Collections.Amount);
        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task CashRequiresConfirmedHandoverAndOneNetSaleMovement()
    {
        var fixture = new Fixture();
        var payment = PaidBill(fixture.RestaurantId, At(2));
        payment.MethodCode = "Cash";
        payment.ProviderTransactionId = null!;
        payment.ProviderPaymentLinkId = null!;
        payment.ProviderOrderCode = null!;
        payment.VerificationSource = "Cashier";
        payment.ConfirmedByRef = Guid.NewGuid();
        payment.CashReceived = 200;
        payment.ChangeAmount = 92;
        payment.CashHandover = new CashHandover
        {
            WaiterUserRef = Guid.NewGuid(),
            CashierUserRef = payment.ConfirmedByRef,
            ConfirmedAt = payment.PaidAt,
            ConfirmedAmount = 200,
            Status = "Confirmed"
        };
        payment.CashMovements.Add(new CashMovement
        {
            Type = "Sale",
            SignedAmount = 108,
            CashShiftId = payment.CashShiftId
        });
        fixture.Repository.Payments.Add(payment);
        Assert.Equal(108m, (await fixture.Get()).Totals.Collections.Amount);
        payment.CashHandover.Status = "Pending";
        var pending = await fixture.Get();
        Assert.Equal(0m, pending.Totals.Collections.KnownAmount);
        Assert.Null(pending.Totals.Collections.Amount);
        payment.CashHandover.Status = "Confirmed";
        payment.CashMovements.Add(new CashMovement
        {
            Type = "Sale",
            SignedAmount = 108
        });
        Assert.Null((await fixture.Get()).Totals.Collections.Amount);
    }

    [Fact]
    public async Task DuplicateProviderReferenceAcrossSourcesIsNotCountedTwice()
    {
        var fixture = new Fixture();
        var dineIn = PaidBill(fixture.RestaurantId, At(2));
        var online = Online(fixture.RestaurantId, At(3));
        online.TransactionReference = dineIn.ProviderTransactionId;
        fixture.Repository.Payments.Add(dineIn);
        fixture.Repository.Online.Add(online);
        var result = await fixture.Get();
        Assert.Equal(0m, result.Totals.Collections.KnownAmount);
        Assert.Null(result.Totals.Collections.Amount);
        Assert.Contains(result.Warnings, value => value.Code == "UnverifiedCollection");
    }

    [Theory]
    [InlineData("day", 2, "2026-11-01")]
    [InlineData("week", 2, "2026-10-26")]
    [InlineData("month", 1, "2026-11-01")]
    public async Task CalendarGroupsUseRestaurantTimeAndMondayWeeks(string grouping, int count, string firstKey)
    {
        var fixture = new Fixture();
        fixture.Repository.Payments.Add(PaidBill(fixture.RestaurantId, new DateTime(2026, 11, 1, 2, 0, 0, DateTimeKind.Utc)));
        fixture.Repository.Payments.Add(PaidBill(fixture.RestaurantId, new DateTime(2026, 11, 2, 2, 0, 0, DateTimeKind.Utc)));
        var result = await fixture.Get(grouping, "2026-10-31T00:00:00Z", "2026-11-04T00:00:00Z");
        Assert.Equal(count, result.Groups.Count);
        Assert.Equal(firstKey, result.Groups[0].Key);
        Assert.Equal(216m, result.Groups.Sum(value => value.Totals.Collections.KnownAmount));
    }

    [Fact]
    public async Task RepeatedDaylightSavingHoursRemainSeparateAndChronological()
    {
        var fixture = new Fixture("America/New_York");
        fixture.Repository.Payments.Add(PaidBill(fixture.RestaurantId, new DateTime(2026, 11, 1, 5, 30, 0, DateTimeKind.Utc)));
        fixture.Repository.Payments.Add(PaidBill(fixture.RestaurantId, new DateTime(2026, 11, 1, 6, 30, 0, DateTimeKind.Utc)));
        var result = await fixture.Get("hour", "2026-11-01T00:00:00Z", "2026-11-02T00:00:00Z");
        Assert.Equal(new[] { "2026-11-01T01:00:00-04:00", "2026-11-01T01:00:00-05:00" }, result.Groups.Select(value => value.Key));
    }

    [Fact]
    public async Task MissingOrderSnapshotNeverUsesBillIdAsOrderCount()
    {
        var fixture = new Fixture();
        var payment = PaidBill(fixture.RestaurantId, At(2));
        payment.Bill.BillLines.Single().ItemSnapshot = "invalid-json";
        fixture.Repository.Payments.Add(payment);
        var result = await fixture.Get();
        Assert.Equal(108m, result.Totals.Collections.Amount);
        Assert.Equal(0, result.Totals.PaidOrders.KnownCount);
        Assert.Null(result.Totals.PaidOrders.Count);
    }

    [Fact]
    public async Task EmptyPeriodHasZeroKnownTotalsAndNoInventedGroups()
    {
        var result = await new Fixture().Get();
        Assert.Equal(0m, result.Totals.Collections.Amount);
        Assert.Equal(0m, result.Totals.DishRevenue.Amount);
        Assert.Equal(0m, result.Totals.NetCollections.KnownAmount);
        Assert.Empty(result.Groups);
        Assert.Null(result.Totals.Refunds.Amount);
    }

    [Fact]
    public async Task RepeatedReadsReturnSameAmountsWithoutChangingSources()
    {
        var fixture = new Fixture();
        fixture.Repository.Payments.Add(PaidBill(fixture.RestaurantId, At(2)));
        var first = await fixture.Get();
        var repeated = await fixture.Get();
        Assert.Equal(first.Totals, repeated.Totals);
        Assert.Single(fixture.Repository.Payments);
        Assert.Equal("Paid", fixture.Repository.Payments[0].Status);
    }

    [Fact]
    public async Task DeniedPeriodAccessNeverReadsFinancialData()
    {
        var fixture = new Fixture
        {
            Denied = true
        };
        await Assert.ThrowsAsync<BillingException>(() => fixture.Get());
        Assert.Equal(0, fixture.Repository.Calls);
    }

    [Theory]
    [InlineData("missing-link")]
    [InlineData("wrong-verification")]
    [InlineData("bill-total-mismatch")]
    [InlineData("two-paid-attempts")]
    public async Task InconsistentSettlementIsExcludedAndMarkedIncomplete(string condition)
    {
        var fixture = new Fixture();
        var payment = PaidBill(fixture.RestaurantId, At(2));
        switch (condition)
        {
            case "missing-link":
                payment.ProviderPaymentLinkId = null!;
                break;
            case "wrong-verification":
                payment.VerificationSource = "Unverified";
                break;
            case "bill-total-mismatch":
                payment.Bill.TotalAmount++;
                break;
            case "two-paid-attempts":
                payment.Bill.Payments.Add(new Payment
                {
                    Status = "Paid"
                });
                break;
        }

        fixture.Repository.Payments.Add(payment);
        var result = await fixture.Get();
        Assert.Equal(0m, result.Totals.Collections.KnownAmount);
        Assert.Null(result.Totals.Collections.Amount);
        Assert.Contains(result.Warnings, value => value.Code == "UnverifiedCollection");
    }

    [Fact]
    public async Task InconsistentBillLinesDoNotEraseVerifiedCollectionsOrInventRevenue()
    {
        var fixture = new Fixture();
        var payment = PaidBill(fixture.RestaurantId, At(2));
        payment.Bill.BillLines.Single().VatAllocated = 9;
        fixture.Repository.Payments.Add(payment);
        var result = await fixture.Get();
        Assert.Equal(108m, result.Totals.Collections.Amount);
        Assert.Equal(0m, result.Totals.DishRevenue.KnownAmount);
        Assert.Null(result.Totals.DishRevenue.Amount);
        Assert.Contains(result.Warnings, value => value.Code == "InconsistentBillRevenue");
    }

    [Fact]
    public async Task RefundedStatusWithoutEvidenceNeverReducesKnownCollections()
    {
        var fixture = new Fixture();
        var payment = Online(fixture.RestaurantId, At(3));
        payment.Status = "Refunded";
        payment.RefundedAt = At(4);
        payment.RefundReference = "reference-without-evidence";
        fixture.Repository.Online.Add(payment);
        var result = await fixture.Get();
        Assert.Equal(50m, result.Totals.Collections.Amount);
        Assert.Equal(0m, result.Totals.Refunds.KnownAmount);
        Assert.Equal(50m, result.Totals.NetCollections.KnownAmount);
        Assert.Contains(result.Warnings, value => value.Code == "UnverifiedRefundRecords");
    }

    [Fact]
    public async Task SalesBreakdownsPartitionCollectionsAndRefundsWithoutAssigningOnlineToAShift()
    {
        var fixture = new Fixture();
        var dineIn = PaidBill(fixture.RestaurantId, At(2));
        var online = Online(fixture.RestaurantId, At(3));
        online.Status = "Refunded";
        online.RefundedAt = At(4);
        online.RefundVerifiedBy = Guid.NewGuid();
        online.RefundReference = "reference";
        online.RefundEvidence = "https://example.test/refund";
        online.OrderId = JsonSerializer.Deserialize<BillItemSnapshot>(dineIn.Bill.BillLines.Single().ItemSnapshot)!.OrderId;
        fixture.Repository.Payments.Add(dineIn);
        fixture.Repository.Online.Add(online);
        var result = await fixture.Get();
        Assert.Equal(2, result.Totals.PaidOrders.Count);
        Assert.Equal(2, result.Channels.Count);
        Assert.Equal(2, result.Shifts.Count);
        Assert.Equal(108m, result.Shifts.Single(value => value.Key == dineIn.CashShiftId.ToString()).Totals.Collections.Amount);
        Assert.Equal(50m, result.Shifts.Single(value => value.Key == "unassigned").Totals.Refunds.KnownAmount);
        foreach (var groups in new[] { result.Channels, result.Methods, result.Shifts })
        {
            Assert.Equal(result.Totals.Collections.KnownAmount, groups.Sum(value => value.Totals.Collections.KnownAmount));
            Assert.Equal(result.Totals.Refunds.KnownAmount, groups.Sum(value => value.Totals.Refunds.KnownAmount));
            Assert.All(groups, value => Assert.Null(value.Totals.NetCollections.Amount));
        }
    }

    [Theory]
    [InlineData("Delivery", "all", "all")]
    [InlineData("all", "Card", "all")]
    [InlineData("all", "all", "active")]
    [InlineData("all", "all", "00000000-0000-0000-0000-000000000000")]
    public async Task InvalidSalesFiltersAreRejectedBeforeFinancialReads(string channel, string method, string shift)
    {
        var fixture = new Fixture();
        var service = new RestaurantSalesReportService(new RestaurantReportService(fixture), fixture.Repository,
            Options.Create(new CashPaymentSettings
            {
                MethodCode = "Cash"
            }), new TestReportOrderClient());
        var error = await Assert.ThrowsAsync<BillingException>(() => service.GetSales(fixture.RestaurantId,
            new ReportPeriodQueryDTO
            {
                From = "2026-11-25T00:00:00Z",
                To = "2026-11-26T00:00:00Z"
            }, default, new SalesReportFilterDTO
            {
                Channel = channel,
                MethodCode = method,
                Shift = shift
            }));
        Assert.Equal(0, fixture.Repository.Calls);
    }

    [Theory]
    [InlineData("DineIn", "DineIn", "DineIn")]
    [InlineData("Takeaway", "Takeaway", "Takeaway")]
    [InlineData("DineIn", "Takeaway", "Mixed")]
    [InlineData("DineIn", "missing", "Unclassified")]
    public async Task ChannelsUseEveryBilledOrderAndNeverSplitMixedMoney(string first, string second, string expected)
    {
        var fixture = new Fixture();
        var payment = PaidBill(fixture.RestaurantId, At(2));
        var firstId = JsonSerializer.Deserialize<BillItemSnapshot>(payment.Bill.BillLines.Single().ItemSnapshot)!.OrderId;
        var secondId = Guid.NewGuid();
        payment.Bill.BillLines.Single().Quantity = .5m;
        payment.Bill.BillLines.Add(new BillLine
        {
            Quantity = .5m,
            UnitPriceSnapshot = 100,
            ItemSnapshot = JsonSerializer.Serialize(new BillItemSnapshot
            {
                OrderId = secondId
            })
        });
        fixture.Channels.Values[firstId] = first;
        if (second != "missing")
        {
            fixture.Channels.Values[secondId] = second;
        }

        fixture.Repository.Payments.Add(payment);
        var all = await fixture.Get();
        var group = Assert.Single(all.Channels);
        Assert.Equal(expected, group.Key);
        Assert.Equal(108m, group.Totals.Collections.Amount);
        Assert.Equal(108m, all.Totals.Collections.Amount);
        Assert.Equal(2, all.Totals.PaidOrders.Count);
        foreach (var filter in new[] { "DineIn", "Takeaway", "Mixed", "Unclassified" })
        {
            var filtered = await fixture.Get(channel: filter);
            Assert.Equal(filter == expected ? 108m : 0m, filtered.Totals.Collections.KnownAmount);
            Assert.Contains(filtered.Warnings, value => value.Code == "ExclusiveChannelFilter");
        }
    }

    [Fact]
    public async Task UnclassifiedBillsMakeKnownChannelBreakdownsIncompleteWithoutInvalidatingOverallCollections()
    {
        var fixture = new Fixture();
        fixture.Repository.Payments.Add(PaidBill(fixture.RestaurantId, At(2)));
        fixture.Repository.Online.Add(Online(fixture.RestaurantId, At(3)));
        var all = await fixture.Get();
        var filtered = await fixture.Get(channel: "Takeaway");
        Assert.Equal(158m, all.Totals.Collections.Amount);
        var takeaway = all.Channels.Single(value => value.Key == "Takeaway").Totals;
        Assert.Equal(50m, takeaway.Collections.KnownAmount);
        Assert.Null(takeaway.Collections.Amount);
        Assert.False(takeaway.Collections.IsComplete);
        Assert.Equal(filtered.Totals, takeaway);
        Assert.Equal(108m, all.Channels.Single(value => value.Key == "Unclassified").Totals.Collections.Amount);
        Assert.Equal(158m, Assert.Single(all.Methods).Totals.Collections.Amount);
        Assert.All(all.Shifts, value => Assert.True(value.Totals.Collections.IsComplete));
    }

    [Fact]
    public async Task SecondBatchTimeoutKeepsAnEntire101OrderBillUnclassified()
    {
        var fixture = new Fixture();
        var payment = PaidBill(fixture.RestaurantId, At(2));
        payment.Bill.BillLines.Clear();
        for (var index = 0; index < 101; index++)
        {
            payment.Bill.BillLines.Add(new BillLine
            {
                Quantity = 1,
                UnitPriceSnapshot = index == 0 ? 100 : 0,
                VatAllocated = index == 0 ? 8 : 0,
                ItemSnapshot = JsonSerializer.Serialize(new BillItemSnapshot
                {
                    OrderId = Guid.NewGuid()
                })
            });
        }

        fixture.Repository.Payments.Add(payment);
        var batch = 0;
        var transport = new RestaurantReportOrderClientTests.Transport((request, _) =>
        {
            batch++;
            if (batch == 2)
            {
                throw new Grpc.Core.RpcException(new Grpc.Core.Status(Grpc.Core.StatusCode.DeadlineExceeded, "test"));
            }

            var reply = new Backend.Contracts.Ordering.OrderReportChannelsReply
            {
                RestaurantId = fixture.RestaurantId.ToString()
            };
            reply.Items.AddRange(request.OrderIds.Select(id => new Backend.Contracts.Ordering.OrderReportChannelInfo
            {
                OrderId = id,
                Channel = "DineIn"
            }));
            return reply;
        });
        var client = RestaurantReportOrderClientTests.Client(transport);
        var all = await fixture.Get(client: client);
        Assert.Equal(108m, all.Totals.Collections.Amount);
        Assert.Equal("Unclassified", Assert.Single(all.Channels).Key);
        batch = 0;
        var filtered = await fixture.Get(channel: "DineIn", client: client);
        Assert.Equal(0m, filtered.Totals.Collections.KnownAmount);
        Assert.Null(filtered.Totals.Collections.Amount);
        Assert.Contains(filtered.Warnings, value => value.Code == "OrderChannelUnavailable");
        Assert.Equal(4, transport.Calls);
    }

    internal static DateTime At(int hour)
    {
        return new DateTime(2026, 11, 25, hour, 0, 0, DateTimeKind.Utc);
    }

    internal static Payment PaidBill(Guid restaurant, DateTime at, decimal subtotal = 100,
        decimal discount = 0, decimal vat = 8, decimal fee = 0)
    {
        var bill = new Bill
        {
            BillId = Guid.NewGuid(),
            RestaurantId = restaurant,
            Status = "Paid",
            PaidAt = at,
            Currency = "VND",
            SubTotal = subtotal,
            DiscountAmount = discount,
            VatAmount = vat,
            ServiceAmount = fee,
            TotalAmount = decimal.Round(subtotal - discount + vat + fee, 0, MidpointRounding.AwayFromZero)
        };
        bill.BillLines.Add(new BillLine
        {
            BillLineId = Guid.NewGuid(),
            Quantity = 1,
            UnitPriceSnapshot = subtotal,
            DiscountAllocated = discount,
            VatAllocated = vat,
            ServiceAllocated = fee,
            ItemSnapshot = JsonSerializer.Serialize(new BillItemSnapshot
            {
                OrderId = Guid.NewGuid()
            })
        });
        var payment = new Payment
        {
            PaymentId = Guid.NewGuid(),
            CashShiftId = Guid.NewGuid(),
            BillId = bill.BillId,
            Bill = bill,
            MethodCode = "BankTransfer",
            Status = "Paid",
            Amount = bill.TotalAmount,
            PaidAt = at,
            IdempotencyKey = "PayOS:" + Guid.NewGuid().ToString("N"),
            ProviderOrderCode = "1",
            ProviderPaymentLinkId = "link",
            ProviderTransactionId = Guid.NewGuid().ToString("N"),
            VerificationSource = "PayOSQuery"
        };
        bill.Payments.Add(payment);
        return payment;
    }

    internal static OnlinePayment Online(Guid restaurant, DateTime at)
    {
        return new OnlinePayment
        {
            RestaurantId = restaurant,
            OrderId = Guid.NewGuid(),
            CustomerUserId = Guid.NewGuid(),
            Amount = 50,
            Status = "Paid",
            PaidAt = at,
            ProviderOrderCode = 2,
            LinkId = "online-link",
            TransactionReference = Guid.NewGuid().ToString("N")
        };
    }

    private sealed class Fixture(string zone = "Asia/Ho_Chi_Minh") : IRestaurantReportContextClient
    {
        public Guid RestaurantId { get; } = Guid.NewGuid();
        public Repository Repository { get; } = new();
        public TestReportOrderClient Channels { get; } = new();
        public bool Denied { get; init; }

        public Task<RestaurantReportContextDTO> GetAsync(Guid restaurantId, CancellationToken ct)
        {
            if (Denied)
            {
                throw new BillingException(403, "Denied.");
            }

            return Task.FromResult(new RestaurantReportContextDTO(RestaurantId, zone));
        }

        public Task<RestaurantSalesReportDTO> Get(string group = "day", string from = "2026-11-25T00:00:00+07:00",
            string to = "2026-11-26T00:00:00+07:00", string channel = "all", IRestaurantReportOrderClient? client = null)
        {
            var service = new RestaurantSalesReportService(new RestaurantReportService(this), Repository,
                Options.Create(new CashPaymentSettings
                {
                    MethodCode = "Cash"
                }), client ?? Channels);
            return service.GetSales(RestaurantId, new ReportPeriodQueryDTO
            {
                From = from,
                To = to,
                GroupBy = group
            }, default, new SalesReportFilterDTO
            {
                Channel = channel
            });
        }
    }

    private sealed class Repository : IRestaurantReportRepository
    {
        public List<Payment> Payments { get; } = [];
        public List<OnlinePayment> Online { get; } = [];
        public int Calls { get; private set; }

        public Task<RestaurantReportSource> GetSourceAsync(Guid restaurantId, DateTime fromUtc, DateTime toUtc, CancellationToken ct, SalesReportFilterDTO? filters = null)
        {
            Calls++;
            return Task.FromResult(new RestaurantReportSource(
                Payments.Where(value => value.PaidAt >= fromUtc && value.PaidAt < toUtc).ToArray(),
                Online.Where(value => value.PaidAt >= fromUtc && value.PaidAt < toUtc).ToArray(),
                Online.Where(value => value.RefundedAt >= fromUtc && value.RefundedAt < toUtc).ToArray(), 0, 0, 0));
        }
    }
}
