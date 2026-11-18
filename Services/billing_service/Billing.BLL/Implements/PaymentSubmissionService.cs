using Billing.BLL.Interfaces;
using Billing.Common.Constants;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.Common.Settings;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.Extensions.Options;
using Shared.Common.Pagination;
using Shared.Persistence.Interfaces;

namespace Billing.BLL.Implements;

public sealed class PaymentSubmissionService(IPaymentReviewClient reviews, IBillingAccess access,
    IBillRepository bills, IBillCheckoutRepository checkouts, ICashRegisterRepository registers,
    ICashShiftRepository shifts, IUnitOfWork unitOfWork, IOptions<CashRegisterAccessSettings> roles,
    TimeProvider clock) : IPaymentSubmissionService
{
    public async Task<PaymentSubmissionDTO> Submit(Guid sessionId, Guid requestId, CancellationToken ct)
    {
        if (sessionId == Guid.Empty || requestId == Guid.Empty)
        {
            throw new BillingException(400, "Yêu cầu mã phiên bàn và yêu cầu thanh toán hợp lệ.");
        }

        var review = await reviews.GetReviewAsync(sessionId, requestId, ct);
        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (!await bills.LockSessionAsync(sessionId, ct))
            {
                throw new BillingException(409, "Phiên bàn đang được xử lý. Vui lòng gửi lại cùng yêu cầu.");
            }

            var existing = await checkouts.GetByIdAsync(requestId, ct);
            if (existing is not null)
            {
                if (existing.TableSessionRef != sessionId || !existing.SubmittedAt.HasValue
                    || !existing.CashRegisterId.HasValue)
                {
                    throw new BillingException(409, "Mã yêu cầu đang được sử dụng cho một lượt checkout khác.");
                }

                return Map(existing);
            }

            if (await checkouts.GetActiveAsync(sessionId, ct) is not null
                || await checkouts.HasPaymentOrFinalBillAsync(sessionId, ct, PayOsPaymentRules.BlockingExpression))
            {
                throw new BillingException(409, "Phiên bàn đã có lượt thanh toán cần xử lý.");
            }

            if (review.PendingOrderCount > 0)
            {
                throw new BillingException(409, "Vẫn còn đơn chờ xác nhận. Phục vụ cần xử lý trước khi chuyển thu ngân.");
            }

            var register = await DefaultCashRegister.LockAsync(registers, review.RestaurantId, ct);
            var now = clock.GetUtcNow().UtcDateTime;
            var checkout = new BillCheckout
            {
                CheckoutId = requestId,
                TableSessionRef = sessionId,
                CashRegisterId = register.CashRegisterId,
                CreatedAt = now,
                SubmittedAt = now,
                Status = "Submitted"
            };
            checkouts.Add(checkout);
            return Map(checkout);
        }, ct);
    }

    public async Task<PagedResult<PaymentSubmissionDTO>> GetIncoming(Guid restaurantId, int page, int pageSize, CancellationToken ct)
    {
        if (restaurantId == Guid.Empty || !new PaginationRequest
        {
            Page = page,
            PageSize = pageSize
        }.IsValid())
        {
            throw new BillingException(400, "Yêu cầu nhà hàng và thông tin phân trang hợp lệ.");
        }

        var actor = await access.GetCurrentScopeAsync(restaurantId, ct);
        if (actor.UserId == Guid.Empty || actor.IsAdministrator
            || !roles.Value.CashierRoleIds.Contains(actor.RoleId, StringComparer.Ordinal)
            || !actor.RestaurantIds.Contains(restaurantId))
        {
            throw new BillingException(403, "Chỉ thu ngân được phân công tại nhà hàng mới được xem yêu cầu đã chuyển.");
        }

        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var register = await DefaultCashRegister.LockAsync(registers, restaurantId, ct);
            var shift = await RequireActiveShift(register.CashRegisterId, ct);
            if (shift.CashierUserRef != actor.UserId)
            {
                throw new BillingException(403, "Chỉ thu ngân đang đứng ca mới được xem yêu cầu đã chuyển vào quầy.");
            }

            return await checkouts.GetIncomingAsync(restaurantId, register.CashRegisterId, page, pageSize, ct);
        }, ct);
    }

    private async Task<CashShift> RequireActiveShift(Guid registerId, CancellationToken ct)
    {
        var current = await shifts.GetUnclosedAsync(registerId, ct);
        if (current.Count != 1 || current[0].Status != CashShiftStatuses.Active)
        {
            throw new BillingException(409, "Quầy mặc định phải có đúng một ca thu ngân đang hoạt động.");
        }

        return current[0];
    }

    private static PaymentSubmissionDTO Map(BillCheckout checkout)
    {
        return new PaymentSubmissionDTO(checkout.CheckoutId, checkout.TableSessionRef, checkout.CashRegisterId!.Value,
            new DateTimeOffset(DateTime.SpecifyKind(checkout.SubmittedAt!.Value, DateTimeKind.Utc)), checkout.Status, checkout.BillId);
    }
}
