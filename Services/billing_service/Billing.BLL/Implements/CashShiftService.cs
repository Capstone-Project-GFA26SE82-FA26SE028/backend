using Billing.BLL.Interfaces;
using Billing.Common.Constants;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.Common.Settings;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.Extensions.Options;
using Shared.Persistence.Interfaces;

namespace Billing.BLL.Implements;

public sealed class CashShiftService(ICashRegisterRepository registers, ICashShiftRepository shifts,
    IUnitOfWork unitOfWork, IBillingAccess access, IOptions<CashRegisterAccessSettings> settings,
    TimeProvider clock, ICashRegisterService registerService) : ICashShiftService
{
    public async Task<CashShiftDTO> Open(Guid cashRegisterId, OpenCashShiftDTO request, CancellationToken cancellationToken)
    {
        var actor = await access.GetCurrentScopeAsync(null, cancellationToken);
        RequireCashier(actor);
        if (cashRegisterId == Guid.Empty)
        {
            throw new BillingException(400, "Mã máy thu ngân không hợp lệ hoặc bị trống.");
        }

        if (request?.OpeningCash is not decimal openingCash || openingCash < 0
            || openingCash > 9999999999999999.99m || decimal.Truncate(openingCash) != openingCash)
        {
            throw new BillingException(400, "Vui lòng nhập số tiền đầu ca nguyên không âm theo giới hạn decimal(18,2). Không làm tròn số tiền lẻ.");
        }

        var result = await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var register = await registers.LockByIdAsync(cashRegisterId, cancellationToken)
                ?? throw new BillingException(404, "Không tìm thấy quầy thu ngân.");
            var currentActor = await access.GetCurrentScopeAsync(register.RestaurantId, cancellationToken);
            RequireCashier(currentActor);
            if (currentActor.UserId != actor.UserId || !currentActor.RestaurantIds.Contains(register.RestaurantId))
            {
                throw new BillingException(403, "Yêu cầu phân công đang hoạt động tại nhà hàng này.");
            }

            var unclosed = await shifts.GetUnclosedAsync(cashRegisterId, cancellationToken);
            if (unclosed.Count != 0)
            {
                throw new BillingException(409, "Máy thu ngân đã có một ca làm việc chưa đóng.");
            }

            var shift = new CashShift
            {
                CashShiftId = Guid.NewGuid(),
                CashRegisterId = register.CashRegisterId,
                CashierUserRef = currentActor.UserId,
                OpeningCash = openingCash,
                OpenedAt = clock.GetUtcNow().UtcDateTime,
                ClosedAt = null,
                Status = CashShiftStatuses.Active
            };
            await shifts.AddAsync(shift);
            return Map(shift, register.RestaurantId);
        }, cancellationToken);
        return result;
    }

    public async Task<CashShiftDTO> GetCurrent(Guid cashRegisterId, CancellationToken cancellationToken)
    {
        var register = await registerService.GetById(cashRegisterId, cancellationToken);
        var unclosed = await shifts.GetUnclosedAsync(cashRegisterId, cancellationToken);
        if (unclosed.Count == 0)
        {
            throw new BillingException(404, "Không có ca làm việc nào đang mở cho máy thu ngân này.");
        }

        if (unclosed.Count != 1 || unclosed[0].Status != CashShiftStatuses.Active)
        {
            throw new BillingException(409, "Máy thu ngân có các bản ghi ca làm việc không nhất quán. Hãy kiểm tra trước khi tiếp tục.");
        }

        return Map(unclosed[0], register.RestaurantId);
    }

    private void RequireCashier(BillingAccessScope actor)
    {
        if (actor.UserId == Guid.Empty)
        {
            throw new BillingException(401, "Yêu cầu phiên đăng nhập nhân viên hợp lệ.");
        }

        if (actor.IsAdministrator || !settings.Value.CashierRoleIds.Contains(actor.RoleId, StringComparer.Ordinal))
        {
            throw new BillingException(403, "Chỉ thu ngân mới có quyền mở ca làm việc.");
        }

        if (actor.RestaurantIds.Count == 0)
        {
            throw new BillingException(403, "Yêu cầu phân công nhà hàng đang hoạt động.");
        }
    }

    private static CashShiftDTO Map(CashShift shift, Guid restaurantId)
    {
        return new CashShiftDTO
        {
            CashShiftId = shift.CashShiftId,
            CashRegisterId = shift.CashRegisterId,
            RestaurantId = restaurantId,
            CashierUserRef = shift.CashierUserRef,
            OpeningCash = shift.OpeningCash,
            OpenedAt = new DateTimeOffset(DateTime.SpecifyKind(shift.OpenedAt, DateTimeKind.Utc)),
            Status = shift.Status
        };
    }
}
