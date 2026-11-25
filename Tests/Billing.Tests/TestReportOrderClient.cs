using Billing.BLL.Interfaces;

namespace Billing.Tests;

internal sealed class TestReportOrderClient : IRestaurantReportOrderClient
{
    public Dictionary<Guid, string> Values { get; } = [];

    public Task<IReadOnlyDictionary<Guid, string>> GetChannelsAsync(Guid restaurantId, Guid[] orderIds, CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyDictionary<Guid, string>>(Values);
    }
}
