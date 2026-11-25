using Billing.Common.DTOs;

namespace Billing.BLL.Interfaces;

public interface IRestaurantSalesReportService
{
    Task<RestaurantSalesReportDTO> GetSales(Guid restaurantId, ReportPeriodQueryDTO query, CancellationToken ct, SalesReportFilterDTO? filters = null);
}
