using System.Data.SqlTypes;
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
using Shared.Common.Billing;
using Shared.Persistence.Interfaces;

namespace Billing.BLL.Implements;

public sealed partial class PaymentService(IPaymentRepository payments, IBillRepository bills,
    IBillCheckoutRepository checkouts, ICashShiftRepository shifts, ICashRegisterRepository registers,
    IPaymentMethodRepository methods, ICashMovementRepository movements, IUnitOfWork unitOfWork,
    IBillingAccess access, IBillService billService, ICheckoutSourceClient ordering,
    IOptions<CashRegisterAccessSettings> roles, IOptions<CashPaymentSettings> settings, TimeProvider clock,
    IPaymentSettlementService settlement) : IPaymentService
{
    private const decimal MaximumMoney = 9999999999999999.99m;

    public async Task<(PaymentDTO Payment, bool Created)> PayCash(Guid billId, string key,
        CreateCashPaymentDTO request, CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        RequireRole(actor, true);
        if (billId == Guid.Empty || request is null || request.CheckoutId == Guid.Empty
            || request.CashShiftId == Guid.Empty || !Guid.TryParse(key, out var requestId) || requestId == Guid.Empty
            || request.CashReceived is not decimal received || received < 0 || received > MaximumMoney
            || decimal.Truncate(received) != received)
        {
            throw new BillingException(400, "Vui lòng cung cấp mã hóa đơn, checkout, ca làm việc hợp lệ, Idempotency-Key UUID và số tiền mặt VND nguyên.");
        }

        key = requestId.ToString("N");
        var initial = await bills.GetDetailsAsync(billId, ct) ?? throw new BillingException(404, "Không tìm thấy hóa đơn.");
        RequireBranch(actor, initial.RestaurantId);
        var sessions = initial.BillLines.Select(line => line.TableSessionRef).Distinct().ToArray();
        if (sessions.Length != 1 || sessions[0] == Guid.Empty)
        {
            throw new BillingException(409, "Hóa đơn phải xác định đúng một phiên bàn trước khi thanh toán.");
        }

        try
        {
            var result = await unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                if (!await payments.LockIdempotencyKeyAsync(key, ct) || !await bills.LockSessionAsync(sessions[0], ct))
                {
                    throw new BillingException(409, "Thanh toán đang bận. Vui lòng thử lại cùng Idempotency-Key.");
                }

                var verified = await access.GetCurrentScopeAsync(initial.RestaurantId, ct);
                RequireRole(verified, true);
                RequireBranch(verified, initial.RestaurantId);
                if (actor.UserId != verified.UserId)
                {
                    throw new BillingException(401, "Phiên đăng nhập nhân viên đã thay đổi. Vui lòng đăng nhập lại.");
                }

                var checkout = await checkouts.GetByIdAsync(request.CheckoutId, ct);
                var active = await checkouts.GetActiveAsync(sessions[0], ct);
                if (checkout is null || checkout.TableSessionRef != sessions[0] || checkout.BillId != billId
                    || checkout.ReleasedAt.HasValue || active?.CheckoutId != checkout.CheckoutId)
                {
                    throw new BillingException(409, "Hóa đơn này không có yêu cầu thanh toán đang hoạt động như yêu cầu.");
                }

                var existing = await payments.GetByKeyAsync(key, ct);
                if (existing is not null)
                {
                    if (existing.BillId != billId || existing.CashShiftId != request.CashShiftId
                        || existing.CashReceived != received
                        || (existing.Status == PaymentStatuses.Paid && existing.ConfirmedByRef != verified.UserId))
                    {
                        throw new BillingException(409, "Idempotency-Key đã được sử dụng cho một yêu cầu thanh toán khác.");
                    }

                    if (existing.Status == PaymentStatuses.Paid)
                    {
                        if (checkout.Status != PaymentStatuses.Paid || existing.Bill.Status != PaymentStatuses.Paid)
                        {
                            throw new BillingException(409, "Thanh toán đã lưu không phải là biên lai tiền mặt hoàn tất. Hãy kiểm tra trước khi thử lại.");
                        }

                        return (Map(existing, checkout.CheckoutId), false);
                    }

                    if (existing.Status != CashHandoverStatuses.PendingPayment || existing.CashHandover is null
                        || existing.CashHandover.Status != CashHandoverStatuses.Pending || existing.CashHandover.WaiterUserRef == Guid.Empty
                        || existing.CashHandover.CashierUserRef.HasValue || existing.CashHandover.ConfirmedAt.HasValue
                        || existing.CashHandover.ConfirmedAmount.HasValue || existing.PaidAt.HasValue || existing.ConfirmedByRef.HasValue
                        || existing.VerificationSource is not null || existing.MethodCode != settings.Value.MethodCode
                        || existing.ProviderOrderCode is not null || existing.ProviderTransactionId is not null
                        || existing.Refunds.Count != 0 || existing.CashMovements.Count != 0)
                    {
                        throw new BillingException(409, "Bản ghi thu hộ không còn chờ bàn giao hoặc có dữ liệu không nhất quán.");
                    }
                }

                if (checkout.Status != "Ready" || string.IsNullOrEmpty(checkout.SourceHash))
                {
                    throw new BillingException(409, "Yêu cầu thanh toán phải ở trạng thái Ready trước khi thu tiền.");
                }

                var shiftInfo = await shifts.GetDetailsAsync(request.CashShiftId, ct)
                    ?? throw new BillingException(404, "Không tìm thấy ca làm việc tiền mặt.");
                RequireBranch(verified, shiftInfo.CashRegister.RestaurantId);
                if (shiftInfo.CashRegister.RestaurantId != initial.RestaurantId)
                {
                    throw new BillingException(409, "Ca làm việc tiền mặt và hóa đơn phải thuộc cùng một nhà hàng.");
                }

                var register = await registers.LockByIdAsync(shiftInfo.CashRegisterId, ct)
                    ?? throw new BillingException(404, "Không tìm thấy máy thu ngân.");
                var shift = await shifts.LockByIdAsync(request.CashShiftId, ct)
                    ?? throw new BillingException(404, "Không tìm thấy ca làm việc tiền mặt.");
                var unclosed = await shifts.GetUnclosedAsync(register.CashRegisterId, ct);
                if (shift.CashierUserRef != verified.UserId)
                {
                    throw new BillingException(403, "Thu ngân chỉ có thể thu tiền trong ca làm việc của chính mình.");
                }

                if (shift.ClosedAt.HasValue || shift.Status != CashShiftStatuses.Active
                    || unclosed.Count != 1 || unclosed[0].CashShiftId != shift.CashShiftId)
                {
                    throw new BillingException(409, "Ca làm việc đã chọn không phải là ca duy nhất đang hoạt động của máy thu ngân này.");
                }

                if (checkout.CashRegisterId != register.CashRegisterId || !checkout.SubmittedAt.HasValue
                    || !checkout.PreparedByRef.HasValue || !checkout.PreparedAt.HasValue || !checkout.PresentedAt.HasValue)
                {
                    throw new BillingException(409, "Phải dùng đúng quầy nhận và phục vụ đã xác nhận đưa hóa đơn cho khách trước khi thanh toán.");
                }

                var bill = await bills.GetForRefreshAsync(billId, ct) ?? throw new BillingException(404, "Không tìm thấy hóa đơn.");
                if (bill.RestaurantId != register.RestaurantId || bill.Status != "Unpaid" || bill.PaidAt.HasValue
                    || bill.Payments.Any(value => value.PaymentId != existing?.PaymentId && PayOsPaymentRules.IsBlocking(value)) || bill.Currency != "VND" || bill.TotalAmount <= 0
                    || bill.TotalAmount > MaximumMoney || decimal.Truncate(bill.TotalAmount) != bill.TotalAmount)
                {
                    throw new BillingException(409, "Chỉ hóa đơn chưa thanh toán, số tiền nguyên dương VND và chưa có giao dịch mới có thể thanh toán tiền mặt.");
                }

                if (received < bill.TotalAmount)
                {
                    throw new BillingException(400, "Tiền khách đưa phải đủ thanh toán toàn bộ hóa đơn.");
                }

                var method = await methods.LockByCodeAsync(settings.Value.MethodCode, ct);
                if (method is null || !method.IsEnabled)
                {
                    throw new BillingException(409, "Phương thức thanh toán tiền mặt chưa được cấu hình hoặc đã bị tắt.");
                }

                var current = await billService.GetById(billId, ct);
                if (current.SourceStatus != "Current")
                {
                    throw new BillingException(current.SourceStatus == "Unavailable" ? 503 : 409,
                        "Nguồn hóa đơn không còn cập nhật. Chưa ghi nhận thanh toán; vui lòng kiểm tra lại đơn trước khi thu tiền mặt.");
                }

                var hash = CheckoutHash.Compute(current.Items.Select(line => new CheckoutHashLine(line.OrderItemRef,
                    line.Item.ItemId, line.Quantity, line.Item.BaseUnitPrice, line.Item.Options.Select(option =>
                        new CheckoutHashOption(option.OptionId, option.Quantity, option.ExtraPrice)).ToArray())));
                if (hash != checkout.SourceHash)
                {
                    throw new BillingException(409, "Hóa đơn không khớp với danh sách món sẵn sàng thanh toán.");
                }

                await ordering.BindAsync(sessions[0], checkout.CheckoutId, billId, hash, ct);
                var expectedCash = shift.OpeningCash + await movements.GetTotalAsync(shift.CashShiftId, ct);
                if (expectedCash < 0 || expectedCash > MaximumMoney || expectedCash + bill.TotalAmount > MaximumMoney)
                {
                    throw new BillingException(409, "Số tiền mặt dự kiến sau giao dịch vượt quá giới hạn hỗ trợ. Chưa ghi nhận thanh toán.");
                }

                var paidAt = DateTime.SpecifyKind(new SqlDateTime(clock.GetUtcNow().UtcDateTime).Value, DateTimeKind.Utc);
                if (existing is null || existing.CashHandover is null)
                {
                    throw new BillingException(409, "Phục vụ phải ghi nhận thu hộ trước khi thu ngân xác nhận nhận tiền.");
                }

                if (existing.Amount != bill.TotalAmount || existing.ChangeAmount != received - bill.TotalAmount
                    || existing.CashHandover.CollectedAt > paidAt)
                {
                    throw new BillingException(409, "Số tiền hoặc thời điểm thu hộ không khớp hóa đơn. Chưa ghi nhận thanh toán.");
                }

                var payment = await payments.GetForCashConfirmationAsync(existing.PaymentId, ct)
                    ?? throw new BillingException(404, "Không tìm thấy khoản thu hộ.");
                payment.Bill = bill;
                payment.CashShift = shift;
                payment.Status = PaymentStatuses.Paid;
                payment.PaidAt = paidAt;
                payment.ConfirmedByRef = verified.UserId;
                payment.VerificationSource = PaymentStatuses.CashierVerification;
                payment.CashHandover.Status = CashHandoverStatuses.Confirmed;
                payment.CashHandover.CashierUserRef = verified.UserId;
                payment.CashHandover.ConfirmedAt = paidAt;
                payment.CashHandover.ConfirmedAmount = received;
                if (await movements.PaymentHasMovementAsync(payment.PaymentId, ct))
                {
                    throw new BillingException(409, "Thanh toán này đã được ghi nhận biến động tiền mặt.");
                }

                await movements.AddAsync(new CashMovement
                {
                    CashMovementId = Guid.NewGuid(),
                    CashShiftId = shift.CashShiftId,
                    PaymentId = payment.PaymentId,
                    Payment = payment,
                    Type = PaymentStatuses.SaleMovement,
                    SignedAmount = payment.Amount,
                    Reason = $"Thanh toán tiền mặt cho hóa đơn {bill.BillNumber}.",
                    PostedAt = paidAt,
                    IdempotencyKey = $"Sale:{payment.PaymentId:N}"
                });
                bill.Status = PaymentStatuses.Paid;
                bill.PaidAt = paidAt;
                checkout.Status = PaymentStatuses.Paid;
                return (Map(payment, checkout.CheckoutId), true);
            }, ct);
            result.Item1.SessionClosureConfirmed = await settlement.TryClose(sessions[0], initial.RestaurantId, ct);
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new BillingException(409, "Dữ liệu thanh toán đã thay đổi. Vui lòng tải lại và thử lại cùng Idempotency-Key.");
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException sql && sql.Number is 1205 or 2601 or 2627)
        {
            throw new BillingException(409, "Hệ thống thanh toán đang bận. Vui lòng thử lại cùng Idempotency-Key.");
        }
        catch (SqlException error) when (error.Number is 1205 or 1222)
        {
            throw new BillingException(409, "Thanh toán đang bận. Vui lòng thử lại cùng Idempotency-Key.");
        }
    }

    public async Task<PaymentDTO> GetById(Guid id, CancellationToken ct)
    {
        var actor = await access.GetCurrentScopeAsync(null, ct);
        RequireRole(actor, false);
        if (id == Guid.Empty)
        {
            throw new BillingException(400, "Mã thanh toán không hợp lệ hoặc bị trống.");
        }

        var payment = await payments.GetDetailsAsync(id, ct) ?? throw new BillingException(404, "Không tìm thấy thanh toán.");
        RequireBranch(actor, payment.Bill.RestaurantId);
        var bill = await bills.GetDetailsAsync(payment.BillId, ct)
            ?? throw new BillingException(409, "Không thể xác minh hóa đơn thanh toán.");
        var sessionIds = bill.BillLines.Select(line => line.TableSessionRef).Distinct().ToArray();
        if (sessionIds.Length != 1)
        {
            throw new BillingException(409, "Thanh toán này không xác định được phiên bàn hợp lệ.");
        }

        var checkout = await checkouts.GetActiveAsync(sessionIds[0], ct);
        if (checkout?.BillId != payment.BillId || checkout.Status != PaymentStatuses.Paid)
        {
            throw new BillingException(409, "Thanh toán này chưa có biên lai checkout hoàn tất.");
        }

        return Map(payment, checkout.CheckoutId);
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
            throw new BillingException(403, write ? "Chỉ thu ngân được phân công mới có thể thu tiền mặt." : "Yêu cầu quyền xem thông tin thanh toán.");
        }
    }

    private static void RequireBranch(BillingAccessScope actor, Guid restaurantId)
    {
        if (!actor.IsAdministrator && !actor.RestaurantIds.Contains(restaurantId))
        {
            throw new BillingException(403, "Yêu cầu phân công đang hoạt động tại nhà hàng này.");
        }
    }

    private static PaymentDTO Map(Payment payment, Guid checkoutId)
    {
        var sale = payment.CashMovements.ToArray();
        if (payment.Status != PaymentStatuses.Paid || payment.CashReceived is not decimal received
            || payment.ChangeAmount is not decimal change || payment.PaidAt is not DateTime paidAt
            || payment.ConfirmedByRef is not Guid cashier || sale.Length != 1
            || sale[0].Type != PaymentStatuses.SaleMovement || sale[0].SignedAmount != payment.Amount
            || sale[0].CashShiftId != payment.CashShiftId || received - change != payment.Amount)
        {
            throw new BillingException(409, "Biên lai tiền mặt đã lưu chưa đầy đủ hoặc không nhất quán. Vui lòng kiểm tra lại trước khi tiếp tục.");
        }

        return new PaymentDTO
        {
            PaymentId = payment.PaymentId,
            BillId = payment.BillId,
            BillNumber = payment.Bill.BillNumber,
            RestaurantId = payment.Bill.RestaurantId,
            CheckoutId = checkoutId,
            CashShiftId = payment.CashShiftId,
            CashRegisterId = payment.CashShift.CashRegisterId,
            MethodCode = payment.MethodCode,
            Status = payment.Status,
            Currency = payment.Bill.Currency,
            Amount = payment.Amount,
            CashReceived = received,
            ChangeAmount = change,
            ConfirmedByRef = cashier,
            PaidAt = new DateTimeOffset(DateTime.SpecifyKind(paidAt, DateTimeKind.Utc)),
            CashMovementId = sale[0].CashMovementId
        };
    }
}
