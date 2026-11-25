using Billing.Common.DTOs;

namespace Billing.BLL.Interfaces;

public interface IRestaurantDishReportService
{
    Task<RestaurantDishReportDTO> GetDishes(Guid restaurantId, ReportPeriodQueryDTO query,
        SalesReportFilterDTO filters, Guid? itemId, CancellationToken ct);
}
