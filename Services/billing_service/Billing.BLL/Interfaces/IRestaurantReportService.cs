using Billing.Common.DTOs;

namespace Billing.BLL.Interfaces;

public interface IRestaurantReportService
{
    Task<ReportPeriodDTO> GetPeriod(Guid restaurantId, ReportPeriodQueryDTO query, CancellationToken ct);
}
