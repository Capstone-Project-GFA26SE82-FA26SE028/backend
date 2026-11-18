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

        var openingCash = OpeningCash(request);

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

            return await OpenForRegister(register, currentActor.UserId, openingCash, cancellationToken);
        }, cancellationToken);
        return result;
    }

    public async Task<CashShiftDTO> OpenDefault(Guid restaurantId, OpenCashShiftDTO request, CancellationToken ct)
    {
        if (restaurantId == Guid.Empty)
        {
            throw new BillingException(400, "Yêu cầu ID nhà hàng hợp lệ.");
        }

        var actor = await access.GetCurrentScopeAsync(restaurantId, ct);
        RequireCashier(actor);
        if (!actor.RestaurantIds.Contains(restaurantId))
        {
            throw new BillingException(403, "Yêu cầu phân công đang hoạt động tại nhà hàng này.");
        }

        var openingCash = OpeningCash(request);
        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (!await registers.LockRestaurantAsync(restaurantId, ct))
            {
                throw new BillingException(409, "Thao tác mở ca đang bận. Vui lòng thử lại.");
            }

            var register = await DefaultCashRegister.FindAsync(registers, restaurantId, ct);
            if (register is not null)
            {
                register = await registers.LockByIdAsync(register.CashRegisterId, ct)
                    ?? throw new BillingException(409, "Quầy mặc định không còn khả dụng.");
            }
            else
            {
                register = new CashRegister
                {
                    CashRegisterId = Guid.NewGuid(),
                    RestaurantId = restaurantId,
                    Code = DefaultCashRegister.Code
                };
                await registers.AddAsync(register);
            }

            return await OpenForRegister(register, actor.UserId, openingCash, ct);
        }, ct);
    }

    private async Task<CashShiftDTO> OpenForRegister(CashRegister register, Guid cashier, decimal openingCash, CancellationToken ct)
    {
        if ((await shifts.GetUnclosedAsync(register.CashRegisterId, ct)).Count != 0)
        {
            throw new BillingException(409, "Máy thu ngân đã có một ca làm việc chưa đóng.");
        }

        var shift = new CashShift
        {
            CashShiftId = Guid.NewGuid(),
            CashRegisterId = register.CashRegisterId,
            CashierUserRef = cashier,
            OpeningCash = openingCash,
            OpenedAt = clock.GetUtcNow().UtcDateTime,
            Status = CashShiftStatuses.Active
        };
        await shifts.AddAsync(shift);
        return Map(shift, register.RestaurantId);
    }

    private static decimal OpeningCash(OpenCashShiftDTO request)
    {
        if (request?.OpeningCash is not decimal amount || amount < 0
            || amount > 9999999999999999.99m || decimal.Truncate(amount) != amount)
        {
            throw new BillingException(400, "Vui lòng nhập số tiền đầu ca nguyên không âm theo giới hạn decimal(18,2). Không làm tròn số tiền lẻ.");
        }

        return amount;
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
