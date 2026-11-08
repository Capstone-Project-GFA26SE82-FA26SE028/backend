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
            throw new BillingException(400, "Nhập số tiền thực tế ActualCash và tiền bàn giao CashHandleOver hợp lệ (từ 0 đến ActualCash).");
        }

        var reason = NormalizeReason(request.DifferenceReason);
        var initial = await FindShift(shiftId, actor, ct);
        if (initial.CashierUserRef != actor.UserId)
        {
            throw new BillingException(403, "Thu ngân chỉ có thể đóng ca làm việc của chính mình.");
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
                    throw new BillingException(409, "Ca đã đóng và báo cáo không nhất quán. Hãy kiểm tra trước khi thử lại.");
                }

                if (previous.ActualCash != request.ActualCash || previous.CashHandleOver != request.CashHandleOver
                    || previous.DifferenceReason != reason)
                {
                    throw new BillingException(409, "Ca này đã có báo cáo chốt ca với các thông số khác.");
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
                    throw new BillingException(409, "Các giao dịch PayOS đã thay đổi trong khi đóng ca. Vui lòng đối soát và thử đóng lại cùng ca.");
                }
            }

            var preview = await Calculate(shift, ct);
            if (!preview.CanClose)
            {
                var summary = string.Join("; ", preview.Blockers.Take(5).Select(value => value.PaymentId.HasValue
                    ? $"{value.Code}: Thanh toán {value.PaymentId}"
                    : value.CashMovementId.HasValue ? $"{value.Code}: Giao dịch tiền mặt {value.CashMovementId}" : value.Code));
                throw new BillingException(409, $"Không thể đóng ca: {summary}. Vui lòng xem đối soát các bản ghi chặn đóng ca.");
            }

            if (request.ActualCash != preview.ExpectedCash && reason is null)
            {
                throw new BillingException(400, "Cần nêu rõ lý do chênh lệch khi thừa hoặc thiếu tiền mặt.");
            }

            var closedAt = DateTime.SpecifyKind(new SqlDateTime(clock.GetUtcNow().UtcDateTime).Value, DateTimeKind.Utc);
            if (closedAt < shift.OpenedAt)
            {
                throw new BillingException(409, "Thời gian đóng ca không thể trước thời gian mở ca.");
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
                    Message = "Ca này đã có báo cáo; không thể ghi đè dữ liệu đóng ca."
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
            throw new BillingException(400, "Mã báo cáo không hợp lệ hoặc bị trống.");
        }

        var report = await reports.GetDetailsAsync(id, ct) ?? throw new BillingException(404, "Không tìm thấy báo cáo ca làm việc.");
        RequireBranch(actor, report.CashShift.CashRegister.RestaurantId);
        return Map(report);
    }

    public async Task<ShiftReportDTO> GetForShift(Guid shiftId, CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        RequireRole(actor, false);
        await FindShift(shiftId, actor, ct);
        return Map(await reports.GetForShiftAsync(shiftId, ct) ?? throw new BillingException(404, "Không tìm thấy báo cáo ca làm việc."));
    }

    public async Task<PagedResult<ShiftReportDTO>> GetAll(Guid? restaurantId, int page, int pageSize, CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        RequireRole(actor, false);
        if (restaurantId == Guid.Empty || page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
        {
            throw new BillingException(400, "Vui lòng cung cấp bộ lọc báo cáo và phân trang hợp lệ (page >= 1, pageSize 1-100).");
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
            throw new BillingException(400, "Mã ca làm việc không hợp lệ hoặc bị trống.");
        }

        var shift = await shifts.GetDetailsAsync(id, ct) ?? throw new BillingException(404, "Không tìm thấy ca thu ngân.");
        RequireBranch(actor, shift.CashRegister.RestaurantId);
        return shift;
    }

    private async Task<CashShift> LockAndAuthorize(CashShift initial, BillingAccessScope actor, bool write, CancellationToken ct)
    {
        var register = await registers.LockByIdAsync(initial.CashRegisterId, ct) ?? throw new BillingException(404, "Không tìm thấy quầy thu ngân.");
        var shift = await shifts.LockByIdAsync(initial.CashShiftId, ct) ?? throw new BillingException(404, "Không tìm thấy ca thu ngân.");
        var verified = await access.GetCurrentScopeAsync(register.RestaurantId, ct);
        RequireRole(verified, write);
        RequireBranch(verified, register.RestaurantId);
        if (actor.UserId != verified.UserId)
        {
            throw new BillingException(401, "Phiên đăng nhập nhân viên đã thay đổi. Vui lòng đăng nhập lại.");
        }

        if (shift.CashRegisterId != register.CashRegisterId || (write && shift.CashierUserRef != verified.UserId))
        {
            throw new BillingException(403, "Thu ngân chỉ có thể đóng ca làm việc của chính mình.");
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
                throw new BillingException(409, $"Giao dịch PayOS {payment.PaymentId} chưa có kết quả xác minh cuối cùng. Ca làm việc chưa được đóng.");
            }

            verified.Add(payment.PaymentId, result);
        }

        return verified;
    }

    private void RequireRole(BillingAccessScope actor, bool write)
    {
        if (actor.UserId == Guid.Empty)
        {
            throw new BillingException(401, "Yêu cầu phiên đăng nhập nhân viên hợp lệ.");
        }

        var cashier = roles.Value.CashierRoleIds.Contains(actor.RoleId, StringComparer.Ordinal);
        var manager = roles.Value.ManagerRoleIds.Contains(actor.RoleId, StringComparer.Ordinal);
        if (write ? actor.IsAdministrator || !cashier : !actor.IsAdministrator && !cashier && !manager)
        {
            throw new BillingException(403, write ? "Chỉ thu ngân mới có quyền đóng ca làm việc." : "Yêu cầu quyền xem thông tin thanh toán.");
        }
    }

    private static void RequireBranch(BillingAccessScope actor, Guid restaurantId)
    {
        if (!actor.IsAdministrator && !actor.RestaurantIds.Contains(restaurantId))
        {
            throw new BillingException(403, "Yêu cầu phân công đang hoạt động tại nhà hàng này.");
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
            throw new BillingException(400, "Lý do chênh lệch tối đa 1000 ký tự và không chứa ký tự điều khiển.");
        }

        var encoding = (Encoding)CodePagesEncodingProvider.Instance.GetEncoding(1252)!.Clone();
        encoding.EncoderFallback = EncoderFallback.ExceptionFallback;
        try
        {
            encoding.GetBytes(reason);
        }
        catch (EncoderFallbackException)
        {
            throw new BillingException(400, "Lý do chênh lệch chứa ký tự không được cột văn bản trong cơ sở dữ liệu hỗ trợ.");
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
            throw new BillingException(409, "Ca làm việc đã thay đổi. Vui lòng tải lại và thử lại cùng dữ liệu đóng ca.");
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException sql && sql.Number is 1205 or 2601 or 2627)
        {
            throw new BillingException(409, "Đóng ca đang bận hoặc đã được ghi nhận. Vui lòng tải lại và thử lại.");
        }
        catch (SqlException error) when (error.Number is 1205 or 1222)
        {
            throw new BillingException(409, "Thao tác đóng ca đang bận. Vui lòng thử lại với cùng dữ liệu đóng ca.");
        }
        catch (OverflowException)
        {
            throw new BillingException(409, "Tổng số tiền đối soát vượt quá giới hạn hỗ trợ.");
        }
    }
}
