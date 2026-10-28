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
            throw new BillingException(400, "A valid cash register ID is required.");
        }

        if (request?.OpeningCash is not decimal openingCash || openingCash < 0
            || openingCash > 9999999999999999.99m || decimal.Truncate(openingCash) != openingCash)
        {
            throw new BillingException(400, "Provide a non-negative whole-VND opening cash amount within decimal(18,2) limits. Fractional amounts are not rounded.");
        }

        var result = await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var register = await registers.LockByIdAsync(cashRegisterId, cancellationToken)
                ?? throw new BillingException(404, "Cash register not found.");
            var currentActor = await access.GetCurrentScopeAsync(register.RestaurantId, cancellationToken);
            RequireCashier(currentActor);
            if (currentActor.UserId != actor.UserId || !currentActor.RestaurantIds.Contains(register.RestaurantId))
            {
                throw new BillingException(403, "An active assignment to this restaurant is required.");
            }

            var unclosed = await shifts.GetUnclosedAsync(cashRegisterId, cancellationToken);
            if (unclosed.Count != 0)
            {
                throw new BillingException(409, "The cash register already has an unclosed shift.");
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
            throw new BillingException(404, "No open cash shift exists for this register.");
        }

        if (unclosed.Count != 1 || unclosed[0].Status != CashShiftStatuses.Active)
        {
            throw new BillingException(409, "The register has inconsistent shift records. Review them before continuing.");
        }

        return Map(unclosed[0], register.RestaurantId);
    }

    private void RequireCashier(BillingAccessScope actor)
    {
        if (actor.UserId == Guid.Empty)
        {
            throw new BillingException(401, "A valid employee session is required.");
        }

        if (actor.IsAdministrator || !settings.Value.CashierRoleIds.Contains(actor.RoleId, StringComparer.Ordinal))
        {
            throw new BillingException(403, "Only a Cashier can open a cash shift.");
        }

        if (actor.RestaurantIds.Count == 0)
        {
            throw new BillingException(403, "An active restaurant assignment is required.");
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
