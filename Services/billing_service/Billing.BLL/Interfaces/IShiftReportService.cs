using Billing.Common.DTOs;
using Shared.Common.Pagination;

namespace Billing.BLL.Interfaces;

public interface IShiftReportService
{
    Task<ShiftReconciliationDTO> Preview(Guid shiftId, CancellationToken ct);
    Task<(ShiftReportDTO Report, bool Created)> Close(Guid shiftId, CloseCashShiftDTO request, CancellationToken ct);
    Task<ShiftReportDTO> GetById(Guid id, CancellationToken ct);
    Task<ShiftReportDTO> GetForShift(Guid shiftId, CancellationToken ct);
    Task<PagedResult<ShiftReportDTO>> GetAll(Guid? restaurantId, int page, int pageSize, CancellationToken ct);
}
