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

public sealed class CashMovementService(ICashMovementRepository movements, ICashShiftRepository shifts,
    ICashRegisterRepository registers, IUnitOfWork unitOfWork, IBillingAccess access,
    IOptions<CashRegisterAccessSettings> roles, TimeProvider clock) : ICashMovementService
{
    public async Task<(CashMovementDTO Movement, bool Created)> Create(Guid shiftId, string key,
        CreateCashMovementDTO request, CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        RequireRole(actor, true);
        if (shiftId == Guid.Empty || !Guid.TryParse(key, out var requestId) || requestId == Guid.Empty
            || request is null || request.Type is not (CashMovementTypes.CashIn or CashMovementTypes.CashOut)
            || request.Amount is not decimal amount || amount <= 0 || amount > 9999999999999999.99m
            || decimal.Truncate(amount) != amount)
        {
            throw new BillingException(400, "Vui lòng cung cấp ca làm việc, Idempotency-Key UUID, loại CashIn/CashOut và số tiền nguyên dương VND.");
        }

        var reason = ValidateReason(request.Reason);
        var initial = await shifts.GetDetailsAsync(shiftId, ct) ?? throw new BillingException(404, "Không tìm thấy ca thu ngân.");
        RequireBranch(actor, initial.CashRegister.RestaurantId);
        key = CashMovementTypes.ManualKeyPrefix + requestId.ToString("N");
        return await Controlled(async () => await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (!await movements.LockIdempotencyKeyAsync(key, ct))
            {
                throw new BillingException(409, "Ghi sổ tiền mặt đang bận. Vui lòng thử lại cùng Idempotency-Key.");
            }

            var register = await registers.LockByIdAsync(initial.CashRegisterId, ct)
                ?? throw new BillingException(404, "Không tìm thấy quầy thu ngân.");
            var shift = await shifts.LockByIdAsync(shiftId, ct) ?? throw new BillingException(404, "Không tìm thấy ca thu ngân.");
            var verified = await access.GetCurrentScopeAsync(register.RestaurantId, ct);
            RequireRole(verified, true);
            RequireBranch(verified, register.RestaurantId);
            if (verified.UserId != actor.UserId)
            {
                throw new BillingException(401, "Phiên đăng nhập nhân viên đã thay đổi. Vui lòng đăng nhập lại.");
            }

            if (shift.CashRegisterId != register.CashRegisterId || shift.CashierUserRef != verified.UserId)
            {
                throw new BillingException(403, "Thu ngân chỉ có thể ghi nhận tiền vào/ra trong ca làm việc của chính mình.");
            }

            var existing = await movements.GetByKeyAsync(key, ct);
            if (existing.Count > 1)
            {
                throw new BillingException(409, "Khóa yêu cầu này có các bản ghi sổ cái không nhất quán. Hãy kiểm tra sổ cái trước khi thử lại.");
            }

            if (existing.Count == 1)
            {
                var value = existing[0];
                var signed = request.Type == CashMovementTypes.CashIn ? amount : -amount;
                if (value.CashShiftId != shiftId || value.PaymentId.HasValue || value.Type != request.Type
                    || value.SignedAmount != signed || value.Reason != reason)
                {
                    throw new BillingException(409, "Idempotency-Key đã được sử dụng cho một biến động tiền mặt khác.");
                }

                return (Map(value), false);
            }

            var unclosed = await shifts.GetUnclosedAsync(register.CashRegisterId, ct);
            if (shift.Status != CashShiftStatuses.Active || shift.ClosedAt.HasValue
                || unclosed.Count != 1 || unclosed[0].CashShiftId != shiftId)
            {
                throw new BillingException(409, "Biến động tiền mặt yêu cầu ca duy nhất đang hoạt động trên máy thu ngân này.");
            }

            var total = await movements.GetTotalAsync(shiftId, ct);
            var expected = checked(shift.OpeningCash + total);
            var afterPosting = expected + (request.Type == CashMovementTypes.CashIn ? amount : -amount);
            if (expected > 9999999999999999.99m || afterPosting > 9999999999999999.99m)
            {
                throw new BillingException(409, "Số tiền mặt dự kiến vượt quá giới hạn decimal(18,2). Chưa ghi nhận biến động; vui lòng kiểm tra số dư ca.");
            }

            if (request.Type == CashMovementTypes.CashOut && amount > expected)
            {
                throw new BillingException(409, "Số tiền rút ra vượt quá số tiền mặt khả dụng hiện có trong ca.");
            }

            var valueToAdd = new CashMovement
            {
                CashMovementId = Guid.NewGuid(),
                CashShiftId = shiftId,
                CashShift = shift,
                Type = request.Type,
                SignedAmount = request.Type == CashMovementTypes.CashIn ? amount : -amount,
                Reason = reason,
                PostedAt = DateTime.SpecifyKind(new SqlDateTime(clock.GetUtcNow().UtcDateTime).Value, DateTimeKind.Utc),
                IdempotencyKey = key
            };
            await movements.AddAsync(valueToAdd);
            return (Map(valueToAdd), true);
        }, ct));
    }

    public async Task<CashLedgerDTO> GetAll(Guid shiftId, int page, int pageSize, CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        RequireRole(actor, false);
        if (shiftId == Guid.Empty || page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
        {
            throw new BillingException(400, "Vui lòng cung cấp ca làm việc và phân trang hợp lệ (page >= 1, pageSize 1-100).");
        }

        var initial = await shifts.GetDetailsAsync(shiftId, ct) ?? throw new BillingException(404, "Không tìm thấy ca thu ngân.");
        RequireBranch(actor, initial.CashRegister.RestaurantId);
        return await Controlled(async () => await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var register = await registers.LockByIdAsync(initial.CashRegisterId, ct)
                ?? throw new BillingException(404, "Không tìm thấy quầy thu ngân.");
            var shift = await shifts.LockByIdAsync(shiftId, ct) ?? throw new BillingException(404, "Không tìm thấy ca thu ngân.");
            var verified = await access.GetCurrentScopeAsync(register.RestaurantId, ct);
            RequireRole(verified, false);
            RequireBranch(verified, register.RestaurantId);
            if (verified.UserId != actor.UserId)
            {
                throw new BillingException(401, "Phiên đăng nhập nhân viên đã thay đổi. Vui lòng đăng nhập lại.");
            }

            var total = await movements.GetTotalAsync(shiftId, ct);
            var result = await movements.GetPageAsync(shiftId, page, pageSize, ct);
            return new CashLedgerDTO
            {
                CashShiftId = shiftId,
                RestaurantId = register.RestaurantId,
                OpeningCash = shift.OpeningCash,
                MovementTotal = total,
                ExpectedCash = checked(shift.OpeningCash + total),
                Movements = new PagedResult<CashMovementDTO>(result.Items.Select(Map).ToArray(), result.TotalCount, page, pageSize)
            };
        }, ct));
    }

    public async Task<CashMovementDTO> GetById(Guid id, CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        RequireRole(actor, false);
        if (id == Guid.Empty)
        {
            throw new BillingException(400, "Mã biến động tiền mặt không hợp lệ hoặc bị trống.");
        }

        var value = await movements.GetDetailsAsync(id, ct) ?? throw new BillingException(404, "Không tìm thấy biến động tiền mặt.");
        RequireBranch(actor, value.CashShift.CashRegister.RestaurantId);
        return Map(value);
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
            throw new BillingException(403, write ? "Chỉ thu ngân mới có quyền ghi nhận biến động tiền mặt." : "Yêu cầu quyền xem thông tin thanh toán.");
        }
    }

    private static void RequireBranch(BillingAccessScope actor, Guid restaurantId)
    {
        if (!actor.IsAdministrator && !actor.RestaurantIds.Contains(restaurantId))
        {
            throw new BillingException(403, "Yêu cầu phân công đang hoạt động tại nhà hàng này.");
        }
    }

    private static string ValidateReason(string? input)
    {
        var reason = input?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000 || reason.Any(char.IsControl))
        {
            throw new BillingException(400, "Lý do là bắt buộc, từ 1-1000 ký tự và không chứa ký tự điều khiển.");
        }

        var encoding = (Encoding)CodePagesEncodingProvider.Instance.GetEncoding(1252)!.Clone();
        encoding.EncoderFallback = EncoderFallback.ExceptionFallback;
        try
        {
            encoding.GetBytes(reason);
        }
        catch (EncoderFallbackException)
        {
            throw new BillingException(400, "Lý do chứa ký tự không được cột văn bản hiện tại trong cơ sở dữ liệu hỗ trợ.");
        }

        return reason;
    }

    private static CashMovementDTO Map(CashMovement value)
    {
        var manual = !value.PaymentId.HasValue && (value.Type is CashMovementTypes.CashIn or CashMovementTypes.CashOut)
            && value.IdempotencyKey.StartsWith(CashMovementTypes.ManualKeyPrefix, StringComparison.Ordinal)
            && Guid.TryParseExact(value.IdempotencyKey[CashMovementTypes.ManualKeyPrefix.Length..], "N", out _);
        var performer = value.PaymentId.HasValue ? value.Payment?.ConfirmedByRef : manual ? value.CashShift.CashierUserRef : (Guid?)null;
        return new CashMovementDTO
        {
            CashMovementId = value.CashMovementId,
            CashShiftId = value.CashShiftId,
            RestaurantId = value.CashShift.CashRegister.RestaurantId,
            PaymentId = value.PaymentId,
            Type = value.Type,
            SignedAmount = value.SignedAmount,
            Reason = value.Reason,
            PostedAt = new DateTimeOffset(DateTime.SpecifyKind(value.PostedAt, DateTimeKind.Utc)),
            PerformedByRef = performer,
            PerformerSource = performer.HasValue ? manual ? "ShiftOwnerDerived" : "PaymentConfirmation" : "Unknown"
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
            throw new BillingException(409, "Ca làm việc đã thay đổi. Vui lòng tải lại và thử lại cùng Idempotency-Key.");
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException sql && sql.Number is 1205 or 2601 or 2627)
        {
            throw new BillingException(409, "Ghi sổ tiền mặt đang bận. Vui lòng thử lại cùng Idempotency-Key.");
        }
        catch (SqlException error) when (error.Number is 1205 or 1222)
        {
            throw new BillingException(409, "Ghi sổ tiền mặt đang bận. Vui lòng thử lại cùng Idempotency-Key.");
        }
        catch (OverflowException)
        {
            throw new BillingException(409, "Tổng số dư sổ cái vượt quá giới hạn hỗ trợ.");
        }
    }
}
