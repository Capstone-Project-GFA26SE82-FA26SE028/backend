using Billing.Common.DTOs;

namespace Billing.BLL.Interfaces;

public interface ICashShiftService
{
    Task<CashShiftDTO> Open(Guid cashRegisterId, OpenCashShiftDTO request, CancellationToken cancellationToken);
    Task<CashShiftDTO> GetCurrent(Guid cashRegisterId, CancellationToken cancellationToken);
}
