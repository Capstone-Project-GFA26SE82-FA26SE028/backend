using Billing.Common.DTOs;
using Billing.DAL.DTOs;

namespace Billing.DAL.Interfaces;

public interface IRestaurantReportRepository
{
    Task<RestaurantReportSource> GetSourceAsync(Guid restaurantId, DateTime fromUtc, DateTime toUtc, CancellationToken ct, SalesReportFilterDTO? filters = null);
}
