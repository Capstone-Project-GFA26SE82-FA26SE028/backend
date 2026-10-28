using System.Data.SqlTypes;
using System.Text;
using Billing.BLL.Interfaces;
using Billing.Common.Constants;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.Common.Settings;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Common.Pagination;
using Shared.Persistence.Interfaces;

namespace Billing.BLL.Implements;

public sealed class ShiftReportService(IShiftReportRepository reports, ICashShiftRepository shifts,
    ICashRegisterRepository registers, IPaymentRepository payments, ICashMovementRepository movements,
    IUnitOfWork unitOfWork, IBillingAccess access, IOptions<CashRegisterAccessSettings> roles,
    IOptions<CashPaymentSettings> cashSettings, TimeProvider clock, IPayOsPaymentService payOs) : IShiftReportService
{
    public async Task<(ShiftReportDTO Report, bool Created)> Close(Guid shiftId, CloseCashShiftDTO request, CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        RequireRole(actor, true);
        if (request is null || !ValidCash(request.ActualCash) || !ValidCash(request.CashHandleOver)
            || request.CashHandleOver > request.ActualCash)
        {
            throw new BillingException(400, "Enter whole-VND ActualCash counted before removal and CashHandleOver between zero and ActualCash.");
        }

        var reason = NormalizeReason(request.DifferenceReason);
        var initial = await FindShift(shiftId, actor, ct);
        if (initial.CashierUserRef != actor.UserId)
        {
            throw new BillingException(403, "Cashiers can close only their own cash shift.");
        }

        var verifiedPayments = initial.Status == CashShiftStatuses.Active
            ? await VerifyBankPayments(shiftId, ct) : new Dictionary<Guid, PayOsPaymentDTO>();
        payments.ResetProviderTracking();
        return await Controlled(async () => await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var shift = await LockAndAuthorize(initial, actor, true, ct);
            var previous = await reports.GetForShiftAsync(shiftId, ct);
            if (previous is not null)
            {
                if (shift.Status != CashShiftStatuses.Closed || shift.ClosedAt != previous.ClosedAt)
                {
                    throw new BillingException(409, "The closed shift and report are inconsistent. Review them before retrying.");
                }

                if (previous.ActualCash != request.ActualCash || previous.CashHandleOver != request.CashHandleOver
                    || previous.DifferenceReason != reason)
                {
                    throw new BillingException(409, "This shift already has a final report with different closing inputs.");
                }

                return (Map(previous), false);
            }

            var currentPayments = await payments.GetForReconciliationAsync(shiftId, ct);
            foreach (var payment in currentPayments.Where(PayOsPaymentRules.IsPayOs))
            {
                if (!verifiedPayments.TryGetValue(payment.PaymentId, out var verified)
                    || payment.Status != verified.Status || payment.Amount != verified.Amount
                    || payment.ProviderTransactionId != verified.ProviderTransactionId
                    || payment.ProviderPaymentLinkId != verified.ProviderPaymentLinkId
                    || payment.PaidAt != verified.PaidAt?.UtcDateTime)
                {
                    throw new BillingException(409, "PayOS payments changed during closing. Reconcile and retry closing the same shift.");
                }
            }

            var preview = await Calculate(shift, ct);
            if (!preview.CanClose)
            {
                var summary = string.Join("; ", preview.Blockers.Take(5).Select(value => value.PaymentId.HasValue
                    ? $"{value.Code}: Payment {value.PaymentId}"
                    : value.CashMovementId.HasValue ? $"{value.Code}: Movement {value.CashMovementId}" : value.Code));
                throw new BillingException(409, $"Shift cannot close: {summary}. Read reconciliation for all blocking records.");
            }

            if (request.ActualCash != preview.ExpectedCash && reason is null)
            {
                throw new BillingException(400, "A difference reason is required for both cash shortages and surpluses.");
            }

            var closedAt = DateTime.SpecifyKind(new SqlDateTime(clock.GetUtcNow().UtcDateTime).Value, DateTimeKind.Utc);
            if (closedAt < shift.OpenedAt)
            {
                throw new BillingException(409, "Closing time cannot precede the shift opening time.");
            }

            var report = new ShiftReport
            {
                ShiftReportId = Guid.NewGuid(),
                CashShiftId = shiftId,
                CashShift = shift,
                ExpectedCash = preview.ExpectedCash,
                ActualCash = request.ActualCash!.Value,
                CashHandleOver = request.CashHandleOver!.Value,
                DifferenceReason = reason,
                ClosedAt = closedAt
            };
            await reports.AddAsync(report);
            shift.Status = CashShiftStatuses.Closed;
            shift.ClosedAt = closedAt;
            return (Map(report), true);
        }, ct));
    }

    public async Task<ShiftReconciliationDTO> Preview(Guid shiftId, CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        RequireRole(actor, false);
        var initial = await FindShift(shiftId, actor, ct);
        return await Controlled(async () => await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var shift = await LockAndAuthorize(initial, actor, false, ct);
            var result = await Calculate(shift, ct);
            if (await reports.GetForShiftAsync(shiftId, ct) is not null && result.CanClose)
            {
                result.CanClose = false;
                result.Blockers.Add(new ReconciliationBlockerDTO
                {
                    Code = "ExistingReport",
                    Message = "This shift already has a report; closing data cannot be overwritten."
                });
            }

            return result;
        }, ct));
    }

    public async Task<ShiftReportDTO> GetById(Guid id, CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        RequireRole(actor, false);
        if (id == Guid.Empty)
        {
            throw new BillingException(400, "A valid report ID is required.");
        }

        var report = await reports.GetDetailsAsync(id, ct) ?? throw new BillingException(404, "Shift report not found.");
        RequireBranch(actor, report.CashShift.CashRegister.RestaurantId);
        return Map(report);
    }

    public async Task<ShiftReportDTO> GetForShift(Guid shiftId, CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        RequireRole(actor, false);
        await FindShift(shiftId, actor, ct);
        return Map(await reports.GetForShiftAsync(shiftId, ct) ?? throw new BillingException(404, "Shift report not found."));
    }

    public async Task<PagedResult<ShiftReportDTO>> GetAll(Guid? restaurantId, int page, int pageSize, CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        RequireRole(actor, false);
        if (restaurantId == Guid.Empty || page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
        {
            throw new BillingException(400, "Provide valid report filters and pagination (page >= 1, pageSize 1-100).");
        }

        if (restaurantId.HasValue)
        {
            RequireBranch(actor, restaurantId.Value);
        }

        var result = await reports.GetPageAsync(restaurantId, actor.IsAdministrator ? null : actor.RestaurantIds, page, pageSize, ct);
        return new PagedResult<ShiftReportDTO>(result.Items.Select(Map).ToArray(), result.TotalCount, page, pageSize);
    }

    private async Task<CashShift> FindShift(Guid id, BillingAccessScope actor, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            throw new BillingException(400, "A valid shift ID is required.");
        }

        var shift = await shifts.GetDetailsAsync(id, ct) ?? throw new BillingException(404, "Cash shift not found.");
        RequireBranch(actor, shift.CashRegister.RestaurantId);
        return shift;
    }

    private async Task<CashShift> LockAndAuthorize(CashShift initial, BillingAccessScope actor, bool write, CancellationToken ct)
    {
        var register = await registers.LockByIdAsync(initial.CashRegisterId, ct) ?? throw new BillingException(404, "Cash register not found.");
        var shift = await shifts.LockByIdAsync(initial.CashShiftId, ct) ?? throw new BillingException(404, "Cash shift not found.");
        var verified = await access.GetCurrentScopeAsync(register.RestaurantId, ct);
        RequireRole(verified, write);
        RequireBranch(verified, register.RestaurantId);
        if (actor.UserId != verified.UserId)
        {
            throw new BillingException(401, "The employee session changed. Sign in again.");
        }

        if (shift.CashRegisterId != register.CashRegisterId || (write && shift.CashierUserRef != verified.UserId))
        {
            throw new BillingException(403, "Cashiers can close only their own cash shift.");
        }

        return shift;
    }

    private async Task<ShiftReconciliationDTO> Calculate(CashShift shift, CancellationToken ct)
    {
        var unclosed = await shifts.GetUnclosedAsync(shift.CashRegisterId, ct);
        var paymentRows = await payments.GetForReconciliationAsync(shift.CashShiftId, ct);
        var movementRows = await movements.GetForShiftAsync(shift.CashShiftId, ct);
        var billPaymentCounts = await payments.GetBillPaymentCountsAsync(shift.CashShiftId, ct, PayOsPaymentRules.BlockingExpression);
        return ShiftReconciliationCalculator.Calculate(shift, paymentRows, movementRows, billPaymentCounts, cashSettings.Value.MethodCode,
            unclosed.Count == 1 && unclosed[0].CashShiftId == shift.CashShiftId);
    }

    private async Task<Dictionary<Guid, PayOsPaymentDTO>> VerifyBankPayments(Guid shiftId, CancellationToken ct)
    {
        var verified = new Dictionary<Guid, PayOsPaymentDTO>();
        var rows = await payments.GetForReconciliationAsync(shiftId, ct);
        foreach (var payment in rows.Where(PayOsPaymentRules.IsPayOs))
        {
            var result = await payOs.Synchronize(payment.PaymentId, ct);
            var settled = result.Status == PaymentStatuses.Paid && result.ProviderAmountPaid == result.Amount
                && result.ProviderAmountRemaining == 0;
            var zero = result.IsVerifiedZero && result.ProviderAmountPaid == 0
                && result.ProviderAmountRemaining == result.Amount;
            if (!settled && !zero)
            {
                throw new BillingException(409, $"PayOS payment {payment.PaymentId} has no current verified final result. The shift was not closed.");
            }

            verified.Add(payment.PaymentId, result);
        }

        return verified;
    }

    private void RequireRole(BillingAccessScope actor, bool write)
    {
        if (actor.UserId == Guid.Empty)
        {
            throw new BillingException(401, "A valid employee session is required.");
        }

        var cashier = roles.Value.CashierRoleIds.Contains(actor.RoleId, StringComparer.Ordinal);
        var manager = roles.Value.ManagerRoleIds.Contains(actor.RoleId, StringComparer.Ordinal);
        if (write ? actor.IsAdministrator || !cashier : !actor.IsAdministrator && !cashier && !manager)
        {
            throw new BillingException(403, write ? "Only a Cashier can close a shift." : "Billing read permission is required.");
        }
    }

    private static void RequireBranch(BillingAccessScope actor, Guid restaurantId)
    {
        if (!actor.IsAdministrator && !actor.RestaurantIds.Contains(restaurantId))
        {
            throw new BillingException(403, "An active assignment to this restaurant is required.");
        }
    }

    private static bool ValidCash(decimal? amount)
    {
        return amount is >= 0 and <= 9999999999999999.99m && decimal.Truncate(amount.Value) == amount.Value;
    }

    private static string? NormalizeReason(string? input)
    {
        var reason = input?.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            return null;
        }

        if (reason.Length > 1000 || reason.Any(char.IsControl))
        {
            throw new BillingException(400, "Difference reason must be at most 1000 characters without controls.");
        }

        var encoding = (Encoding)CodePagesEncodingProvider.Instance.GetEncoding(1252)!.Clone();
        encoding.EncoderFallback = EncoderFallback.ExceptionFallback;
        try
        {
            encoding.GetBytes(reason);
        }
        catch (EncoderFallbackException)
        {
            throw new BillingException(400, "Difference reason contains characters unsupported by the database text column.");
        }

        return reason;
    }

    private static ShiftReportDTO Map(ShiftReport report)
    {
        return new ShiftReportDTO
        {
            ShiftReportId = report.ShiftReportId,
            CashShiftId = report.CashShiftId,
            CashRegisterId = report.CashShift.CashRegisterId,
            RestaurantId = report.CashShift.CashRegister.RestaurantId,
            CashierUserRef = report.CashShift.CashierUserRef,
            ExpectedCash = report.ExpectedCash,
            ActualCash = report.ActualCash,
            CashHandleOver = report.CashHandleOver,
            Difference = report.ActualCash - report.ExpectedCash,
            RemainingCash = report.ActualCash - report.CashHandleOver,
            DifferenceReason = report.DifferenceReason,
            ClosedAt = new DateTimeOffset(DateTime.SpecifyKind(report.ClosedAt, DateTimeKind.Utc))
        };
    }

    private static async Task<T> Controlled<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BillingException(409, "Shift changed. Reload and retry the same closing inputs.");
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException sql && sql.Number is 1205 or 2601 or 2627)
        {
            throw new BillingException(409, "Shift close is busy or already recorded. Reload and retry the same closing inputs.");
        }
        catch (SqlException error) when (error.Number is 1205 or 1222)
        {
            throw new BillingException(409, "Shift close is busy. Retry the same closing inputs.");
        }
        catch (OverflowException)
        {
            throw new BillingException(409, "Reconciliation totals exceed the supported amount range.");
        }
    }
}
