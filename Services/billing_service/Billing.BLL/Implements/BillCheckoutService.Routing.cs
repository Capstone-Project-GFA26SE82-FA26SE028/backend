using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.DAL.Models;
using Shared.Common.Pagination;

namespace Billing.BLL.Implements;

public sealed partial class BillCheckoutService
{
    public async Task<BillCheckoutDTO> Submit(Guid sessionId, Guid id, Guid cashRegisterId, CancellationToken ct)
    {
        ValidateIds(sessionId, id);
        if (cashRegisterId == Guid.Empty)
        {
            throw new BillingException(400, "Vui lòng chọn quầy thu ngân hợp lệ.");
        }

        await RequireResponsibleWaiter(sessionId, id, ct);
        await Request(sessionId, id, string.Empty, ct);
        return await InSession(sessionId, async () =>
        {
            var actor = await RequireResponsibleWaiter(sessionId, id, ct);
            var current = await Find(sessionId, id, ct);
            if (current.ReleasedAt.HasValue || current.Status is not ("Frozen" or "Ready") || string.IsNullOrEmpty(current.SourceHash))
            {
                throw new BillingException(409, "Phải kiểm tra xong các món và khóa tập món trước khi chuyển quầy.");
            }

            if (current.SubmittedAt.HasValue)
            {
                if (current.CashRegisterId != cashRegisterId)
                {
                    throw new BillingException(409, "Yêu cầu đã chuyển đến quầy khác. Không được âm thầm đổi quầy.");
                }

                return Map(current);
            }

            var register = await registers.LockByIdAsync(cashRegisterId, ct)
                ?? throw new BillingException(404, "Không tìm thấy quầy thu ngân.");
            var snapshot = await source.GetBySessionAsync(sessionId, ct);
            if (register.RestaurantId != snapshot.RestaurantId || !actor.RestaurantIds.Contains(register.RestaurantId))
            {
                throw new BillingException(403, "Quầy nhận phải thuộc đúng nhà hàng của phiên bàn.");
            }

            await RequireActiveShift(register.CashRegisterId, null, ct);
            current.CashRegisterId = cashRegisterId;
            current.SubmittedAt = clock.GetUtcNow().UtcDateTime;
            return Map(current);
        }, ct);
    }

    public async Task<PagedResult<BillCheckoutDTO>> GetIncoming(Guid cashRegisterId, int page, int pageSize, CancellationToken ct)
    {
        if (cashRegisterId == Guid.Empty || !new PaginationRequest
        {
            Page = page,
            PageSize = pageSize
        }.IsValid())
        {
            throw new BillingException(400, "Vui lòng cung cấp quầy và phân trang hợp lệ.");
        }

        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var register = await registers.LockByIdAsync(cashRegisterId, ct)
                ?? throw new BillingException(404, "Không tìm thấy quầy thu ngân.");
            var actor = await access.GetCurrentScopeAsync(register.RestaurantId, ct);
            RequireCashierScope(actor, register.RestaurantId);
            await RequireActiveShift(cashRegisterId, actor.UserId, ct);
            var total = await checkouts.CountIncomingAsync(cashRegisterId, ct);
            var items = await checkouts.GetIncomingAsync(cashRegisterId, page, pageSize, ct);
            return new PagedResult<BillCheckoutDTO>(items.Select(Map).ToArray(), total, page, pageSize);
        }, ct);
    }

    public async Task<BillCheckoutDTO> Present(Guid sessionId, Guid id, CancellationToken ct)
    {
        ValidateIds(sessionId, id);
        return await InSession(sessionId, async () =>
        {
            await RequireResponsibleWaiter(sessionId, id, ct);
            var current = await Find(sessionId, id, ct);
            if (current.ReleasedAt.HasValue || current.Status is not ("Ready" or "Paid")
                || !current.BillId.HasValue || !current.PreparedAt.HasValue || !current.PreparedByRef.HasValue)
            {
                throw new BillingException(409, "Thu ngân phải chuẩn bị hóa đơn trước khi phục vụ đưa cho khách.");
            }

            if (current.PresentedAt.HasValue)
            {
                return Map(current);
            }

            var prebill = await billService.GetPrebill(sessionId, id, ct);
            if (prebill.SourceStatus != "Current")
            {
                throw new BillingException(409, "Hóa đơn chưa đối chiếu khớp đơn hàng. Vui lòng kiểm tra trước khi đưa khách.");
            }

            current.PresentedAt = clock.GetUtcNow().UtcDateTime;
            return Map(current);
        }, ct);
    }

    private async Task<BillingAccessScope> RequireResponsibleWaiter(Guid sessionId, Guid id, CancellationToken ct)
    {
        var actor = await RequireStaff(sessionId, true, ct);
        if (!roles.Value.WaiterRoleIds.Contains(actor.RoleId, StringComparer.Ordinal)
            || await paymentRequests.GetReviewedWaiterAsync(sessionId, id, ct) != actor.UserId)
        {
            throw new BillingException(403, "Chỉ phục vụ đã nhận và kiểm tra yêu cầu mới được thực hiện thao tác này.");
        }

        return actor;
    }

    private async Task<BillingAccessScope> RequireReceivingCashier(BillCheckout current, CancellationToken ct)
    {
        var actor = await RequireStaff(current.TableSessionRef, false, ct);
        if (!current.CashRegisterId.HasValue || !current.SubmittedAt.HasValue)
        {
            throw new BillingException(409, "Phục vụ phải chuyển yêu cầu đến quầy trước khi thu ngân chốt hóa đơn.");
        }

        var register = await registers.LockByIdAsync(current.CashRegisterId.Value, ct)
            ?? throw new BillingException(404, "Không tìm thấy quầy nhận yêu cầu.");
        var snapshot = await source.GetBySessionAsync(current.TableSessionRef, ct);
        RequireCashierScope(actor, register.RestaurantId);
        if (snapshot.RestaurantId != register.RestaurantId)
        {
            throw new BillingException(409, "Quầy nhận không thuộc nhà hàng của phiên bàn.");
        }

        await RequireActiveShift(register.CashRegisterId, actor.UserId, ct);
        return actor;
    }

    private void RequireCashierScope(BillingAccessScope actor, Guid restaurantId)
    {
        if (actor.UserId == Guid.Empty || actor.IsAdministrator
            || !roles.Value.CashierRoleIds.Contains(actor.RoleId, StringComparer.Ordinal)
            || !actor.RestaurantIds.Contains(restaurantId))
        {
            throw new BillingException(403, "Chỉ thu ngân được phân công tại nhà hàng mới được nhận yêu cầu.");
        }
    }

    private async Task RequireActiveShift(Guid cashRegisterId, Guid? cashierId, CancellationToken ct)
    {
        var open = await shifts.GetUnclosedAsync(cashRegisterId, ct);
        if (open.Count != 1)
        {
            throw new BillingException(409, "Quầy phải có đúng một ca đang mở.");
        }

        var shift = await shifts.LockByIdAsync(open[0].CashShiftId, ct);
        if (shift is null || shift.CashRegisterId != cashRegisterId || shift.Status != "Active" || shift.ClosedAt.HasValue)
        {
            throw new BillingException(409, "Ca của quầy nhận không còn hoạt động.");
        }

        if (cashierId.HasValue && shift.CashierUserRef != cashierId.Value)
        {
            throw new BillingException(403, "Chỉ thu ngân sở hữu ca đang mở của quầy nhận mới được chốt hóa đơn.");
        }
    }

    private static DateTimeOffset? Utc(DateTime? value)
    {
        return value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : null;
    }
}
