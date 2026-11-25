namespace Billing.BLL.Interfaces;

public interface IRestaurantReportOrderClient
{
    Task<IReadOnlyDictionary<Guid, string>> GetChannelsAsync(Guid restaurantId, Guid[] orderIds, CancellationToken ct);
}
