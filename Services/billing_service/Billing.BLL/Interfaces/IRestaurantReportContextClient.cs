using Billing.Common.DTOs;

namespace Billing.BLL.Interfaces;

public interface IRestaurantReportContextClient
{
    Task<RestaurantReportContextDTO> GetAsync(Guid restaurantId, CancellationToken ct);
}
