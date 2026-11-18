using Billing.Common.Constants;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.DAL.Models;

namespace Billing.BLL.Implements;

public sealed partial class BillCheckoutService
{
    public async Task<PreparedBillDTO> Prepare(Guid sessionId, Guid id, CancellationToken ct)
    {
        ValidateIds(sessionId, id);
        var prepared = await AsReceivingCashier(sessionId, id, ReadIfPrepared, ct);
        if (prepared is not null)
        {
            return prepared;
        }

        var remote = await ordering.BeginAsync(sessionId, id, string.Empty, ct);
        prepared = await AsReceivingCashier(sessionId, id, async (current, _, token) =>
        {
            if (current.PreparedAt.HasValue)
            {
                return await ReadPrepared(current, token);
            }

            if (remote.Status is not ("Frozen" or "WaitingConfirmation"))
            {
                throw new BillingException(409, "Lượt khóa gọi món không còn hiệu lực.");
            }

            if (current.Status == "Frozen" && (remote.Status != "Frozen" || current.SourceHash != remote.SourceHash))
            {
                throw new BillingException(409, "Bản chụp món đã khóa không nhất quán.");
            }

            current.Status = remote.Status;
            current.SourceHash = remote.SourceHash;
            return null;
        }, ct);
        if (prepared is not null)
        {
            return prepared;
        }

        var hash = remote.SourceHash;
        if (remote.Status != "Frozen" || string.IsNullOrWhiteSpace(hash))
        {
            throw new BillingException(409, "Phiên bàn còn đơn chờ xác nhận. Hãy xử lý đơn và chốt lại cùng yêu cầu.");
        }

        BillDTO bill;
        try
        {
            bill = await BuildBill(sessionId, id, ct);
        }
        catch (BillingException error) when (error.StatusCode == 409)
        {
            prepared = await AsReceivingCashier(sessionId, id, ReadIfPrepared, ct);
            if (prepared is not null)
            {
                return prepared;
            }

            throw;
        }

        if (BillHash(bill) != hash)
        {
            throw new BillingException(409, "Số tiền hóa đơn không khớp với tập món đã khóa.");
        }

        await ordering.BindAsync(sessionId, id, bill.BillId, hash, ct);
        return await AsReceivingCashier(sessionId, id, async (current, cashier, token) =>
        {
            if (current.PreparedAt.HasValue)
            {
                return await ReadPrepared(current, token);
            }

            if (current.Status != "Frozen" || current.SourceHash != hash)
            {
                throw new BillingException(409, "Yêu cầu đã thay đổi trong khi chốt hóa đơn. Hãy kiểm tra lại.");
            }

            var stored = await ReadUnpaidBill(bill.BillId, sessionId, token);
            if (BillHash(stored) != hash)
            {
                throw new BillingException(409, "Hóa đơn đã thay đổi trong khi chốt.");
            }

            current.BillId = stored.BillId;
            current.PreparedByRef = cashier;
            current.PreparedAt = clock.GetUtcNow().UtcDateTime;
            current.Status = "Prepared";
            return MapPrepared(current, stored);
        }, ct);
    }

    public Task<PreparedBillDTO> GetPrepared(Guid sessionId, Guid id, CancellationToken ct)
    {
        return ForResponsibleWaiter(sessionId, id, present: false, ct);
    }

    public Task<PreparedBillDTO> Present(Guid sessionId, Guid id, CancellationToken ct)
    {
        return ForResponsibleWaiter(sessionId, id, present: true, ct);
    }

    private async Task<PreparedBillDTO> ForResponsibleWaiter(Guid sessionId, Guid id, bool present, CancellationToken ct)
    {
        ValidateIds(sessionId, id);
        var review = await reviews.GetReviewAsync(sessionId, id, ct);
        return await InSession(sessionId, async () =>
        {
            var current = await Find(sessionId, id, ct);
            RequireRouted(current);
            var result = await ReadPrepared(current, ct);
            if (result.Bill.RestaurantId != review.RestaurantId)
            {
                throw new BillingException(403, "Hóa đơn không thuộc nhà hàng của yêu cầu thanh toán.");
            }

            if (!present || current.PresentedAt.HasValue)
            {
                return result;
            }

            current.PresentedAt = clock.GetUtcNow().UtcDateTime;
            current.Status = "Presented";
            return MapPrepared(current, result.Bill);
        }, ct);
    }

    private async Task<T> AsReceivingCashier<T>(Guid sessionId, Guid id,
        Func<BillCheckout, Guid, CancellationToken, Task<T>> action, CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        if (actor.UserId == Guid.Empty || actor.IsAdministrator
            || !roles.Value.CashierRoleIds.Contains(actor.RoleId, StringComparer.Ordinal))
        {
            throw new BillingException(403, "Chỉ thu ngân đang đứng ca mới được chốt hóa đơn tại quầy.");
        }

        return await InSession(sessionId, async () =>
        {
            var current = await Find(sessionId, id, ct);
            RequireRouted(current);
            var register = await registers.LockByIdAsync(current.CashRegisterId!.Value, ct)
                ?? throw new BillingException(409, "Quầy thu ngân của yêu cầu không còn khả dụng.");
            var verified = await access.GetCurrentScopeAsync(register.RestaurantId, ct);
            if (verified.UserId != actor.UserId || verified.IsAdministrator
                || !roles.Value.CashierRoleIds.Contains(verified.RoleId, StringComparer.Ordinal)
                || !verified.RestaurantIds.Contains(register.RestaurantId))
            {
                throw new BillingException(403, "Thu ngân không còn được phân công tại nhà hàng của quầy.");
            }

            var unclosed = await shifts.GetUnclosedAsync(register.CashRegisterId, ct);
            if (unclosed.Count != 1 || unclosed[0].Status != CashShiftStatuses.Active
                || unclosed[0].CashierUserRef != actor.UserId)
            {
                throw new BillingException(403, "Yêu cầu ca thu ngân của chính bạn đang hoạt động tại quầy.");
            }

            return await action(current, actor.UserId, ct);
        }, ct);
    }

    private static void RequireRouted(BillCheckout checkout)
    {
        if (!checkout.SubmittedAt.HasValue || !checkout.CashRegisterId.HasValue || checkout.ReleasedAt.HasValue
            || checkout.Status is not ("Submitted" or "WaitingConfirmation" or "Frozen" or "Prepared" or "Presented"))
        {
            throw new BillingException(409, "Yêu cầu chưa được chuyển quầy hoặc không còn hiệu lực.");
        }
    }

    private async Task<PreparedBillDTO?> ReadIfPrepared(BillCheckout checkout, Guid _, CancellationToken ct)
    {
        return checkout.PreparedAt.HasValue ? await ReadPrepared(checkout, ct) : null;
    }

    private async Task<PreparedBillDTO> ReadPrepared(BillCheckout checkout, CancellationToken ct)
    {
        if (checkout.Status is not ("Prepared" or "Presented") || !checkout.BillId.HasValue
            || !checkout.PreparedAt.HasValue || !checkout.PreparedByRef.HasValue)
        {
            throw new BillingException(409, "Thu ngân chưa chốt hóa đơn cho yêu cầu này.");
        }

        var bill = await ReadUnpaidBill(checkout.BillId.Value, checkout.TableSessionRef, ct);
        if (BillHash(bill) != checkout.SourceHash)
        {
            throw new BillingException(409, "Hóa đơn đã lưu không khớp với bản chụp món.");
        }

        return MapPrepared(checkout, bill);
    }

    private async Task<BillDTO> ReadUnpaidBill(Guid billId, Guid sessionId, CancellationToken ct)
    {
        var stored = await bills.GetDetailsAsync(billId, ct)
            ?? throw new BillingException(404, "Không tìm thấy hóa đơn của yêu cầu.");
        if (stored.Status != "Unpaid" || stored.PaidAt.HasValue || stored.BillLines.Count == 0
            || stored.BillLines.Any(line => line.TableSessionRef != sessionId))
        {
            throw new BillingException(409, "Hóa đơn không còn chưa thanh toán hoặc không khớp với phiên bàn.");
        }

        return BillService.Map(stored);
    }

    private static PreparedBillDTO MapPrepared(BillCheckout checkout, BillDTO bill)
    {
        return new PreparedBillDTO(checkout.CheckoutId, checkout.TableSessionRef, checkout.Status, bill,
            checkout.PreparedByRef!.Value, AsUtc(checkout.PreparedAt!.Value),
            checkout.PresentedAt is DateTime presented ? AsUtc(presented) : null);
    }

    private static DateTimeOffset AsUtc(DateTime value)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
