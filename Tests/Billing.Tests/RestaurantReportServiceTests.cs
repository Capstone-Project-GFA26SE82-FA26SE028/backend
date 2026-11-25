using Billing.BLL.Implements;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;

namespace Billing.Tests;

public sealed class RestaurantReportServiceTests
{
    [Theory]
    [InlineData("hour")]
    [InlineData("day")]
    [InlineData("week")]
    [InlineData("month")]
    public async Task PeriodUsesRestaurantTimeZoneAndPreservesExactInstants(string group)
    {
        var context = new Context();
        var result = await new RestaurantReportService(context).GetPeriod(context.RestaurantId, Query(group), default);
        Assert.Equal(DateTimeOffset.Parse("2026-11-24T17:00:00Z"), result.FromUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-11-25T17:00:00Z"), result.ToUtc);
        Assert.Equal(TimeSpan.FromHours(7), result.FromLocal.Offset);
        Assert.Equal(25, result.FromLocal.Day);
        Assert.Equal("Asia/Ho_Chi_Minh", result.TimeZone);
        Assert.Equal(group, result.GroupBy);
        Assert.Equal("Monday", result.WeekStartsOn);
    }

    [Fact]
    public async Task ExplicitOffsetsDisambiguateRepeatedDaylightSavingHour()
    {
        var context = new Context
        {
            TimeZone = "America/New_York"
        };
        var result = await new RestaurantReportService(context).GetPeriod(context.RestaurantId, new ReportPeriodQueryDTO
        {
            From = "2026-11-01T01:30:00-04:00",
            To = "2026-11-01T01:30:00-05:00",
            GroupBy = "hour"
        }, default);
        Assert.Equal(TimeSpan.FromHours(1), result.ToUtc - result.FromUtc);
        Assert.NotEqual(result.FromLocal.Offset, result.ToLocal.Offset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-11-25")]
    [InlineData("2026-11-25T00:00:00")]
    [InlineData("2026-13-25T00:00:00Z")]
    [InlineData("2026-11-25T00:00:00+99:00")]
    public async Task MissingOrAmbiguousTimeIsRejected(string? from)
    {
        var context = new Context();
        var query = Query();
        query.From = from;
        var error = await Assert.ThrowsAsync<BillingException>(() =>
            new RestaurantReportService(context).GetPeriod(context.RestaurantId, query, default));
        Assert.Equal(400, error.StatusCode);
    }

    [Theory]
    [InlineData("2026-11-26T00:00:00+07:00")]
    [InlineData("2026-11-27T00:00:00+07:00")]
    public async Task EqualOrReversedBoundsAreRejected(string from)
    {
        var context = new Context();
        var query = Query();
        query.From = from;
        var error = await Assert.ThrowsAsync<BillingException>(() =>
            new RestaurantReportService(context).GetPeriod(context.RestaurantId, query, default));
        Assert.Equal(400, error.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("year")]
    public async Task UnsupportedGroupingIsRejected(string? group)
    {
        var context = new Context();
        var query = Query();
        query.GroupBy = group;
        var error = await Assert.ThrowsAsync<BillingException>(() =>
            new RestaurantReportService(context).GetPeriod(context.RestaurantId, query, default));
        Assert.Equal(400, error.StatusCode);
    }

    [Fact]
    public async Task InvalidRestaurantTimeZoneDoesNotFallBackToServerTimeZone()
    {
        var context = new Context
        {
            TimeZone = "unknown/restaurant-zone"
        };
        var error = await Assert.ThrowsAsync<BillingException>(() =>
            new RestaurantReportService(context).GetPeriod(context.RestaurantId, Query(), default));
        Assert.Equal(503, error.StatusCode);
    }

    [Fact]
    public async Task DeniedAccessStopsBeforePeriodValidation()
    {
        var context = new Context
        {
            Denied = true
        };
        var error = await Assert.ThrowsAsync<BillingException>(() =>
            new RestaurantReportService(context).GetPeriod(context.RestaurantId, new ReportPeriodQueryDTO(), default));
        Assert.Equal(403, error.StatusCode);
    }

    [Fact]
    public async Task EmptyIdIsRejectedWithoutCallingRestaurant()
    {
        var context = new Context();
        var error = await Assert.ThrowsAsync<BillingException>(() =>
            new RestaurantReportService(context).GetPeriod(Guid.Empty, Query(), default));
        Assert.Equal(400, error.StatusCode);
        Assert.Equal(0, context.Calls);
    }

    [Fact]
    public async Task WrongContextOwnerIsRejected()
    {
        var context = new Context
        {
            ReturnedRestaurantId = Guid.NewGuid()
        };
        var error = await Assert.ThrowsAsync<BillingException>(() =>
            new RestaurantReportService(context).GetPeriod(context.RestaurantId, Query(), default));
        Assert.Equal(503, error.StatusCode);
    }

    [Fact]
    public async Task UtcFractionalSecondsArePreservedWithoutRounding()
    {
        var context = new Context();
        var query = Query();
        query.From = "2026-11-25T00:00:00.1234567Z";
        var result = await new RestaurantReportService(context).GetPeriod(context.RestaurantId, query, default);
        Assert.Equal(DateTimeOffset.Parse(query.From), result.FromUtc);
    }

    [Theory]
    [InlineData("2026-11-25T00:00:00z")]
    [InlineData("2026-11-25T00:00:00.1234567z")]
    public async Task LowercaseUtcDesignatorMatchesUppercaseUtc(string from)
    {
        var context = new Context();
        var service = new RestaurantReportService(context);
        var lowercaseQuery = Query();
        lowercaseQuery.From = from;
        var uppercaseQuery = Query();
        uppercaseQuery.From = from[..^1] + "Z";
        var expected = await service.GetPeriod(context.RestaurantId, uppercaseQuery, default);
        var actual = await service.GetPeriod(context.RestaurantId, lowercaseQuery, default);
        Assert.Equal(expected, actual);
        Assert.Equal(from, lowercaseQuery.From);
    }

    private static ReportPeriodQueryDTO Query(string group = "day")
    {
        return new ReportPeriodQueryDTO
        {
            From = "2026-11-25T00:00:00+07:00",
            To = "2026-11-26T00:00:00+07:00",
            GroupBy = group
        };
    }

    private sealed class Context : IRestaurantReportContextClient
    {
        public Guid RestaurantId { get; } = Guid.NewGuid();
        public string TimeZone { get; init; } = "Asia/Ho_Chi_Minh";
        public bool Denied { get; init; }
        public Guid? ReturnedRestaurantId { get; init; }
        public int Calls { get; private set; }

        public Task<RestaurantReportContextDTO> GetAsync(Guid restaurantId, CancellationToken ct)
        {
            Calls++;
            if (Denied)
            {
                throw new BillingException(403, "Denied by Restaurant.");
            }

            return Task.FromResult(new RestaurantReportContextDTO(ReturnedRestaurantId ?? RestaurantId, TimeZone));
        }
    }
}
