using Billing.Common.DTOs;

namespace Billing.BLL.Interfaces;

public interface IRestaurantReportItemsClient
{
    Task<IReadOnlyDictionary<Guid, ReportOrderSnapshotDTO>> GetItemsAsync(Guid restaurantId, Guid[] orderIds, CancellationToken ct);
}
