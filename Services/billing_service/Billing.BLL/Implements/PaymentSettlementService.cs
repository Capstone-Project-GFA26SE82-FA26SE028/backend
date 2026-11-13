using Billing.BLL.Interfaces;
using Billing.Common.Constants;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.Common.Settings;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Common.Billing;
using Shared.Persistence.Interfaces;
using System.Text.Json;

namespace Billing.BLL.Implements;

public sealed class PaymentSettlementService(IPaymentRepository payments, IBillRepository bills,
    IBillCheckoutRepository checkouts, IUnitOfWork unitOfWork, IOptions<CashPaymentSettings> settings,
    ISessionSettlementClient client, ILogger<PaymentSettlementService> logger) : IPaymentSettlementService
{
    public async Task<PaidSessionSettlement> GetPaid(Guid sessionId, Guid restaurantId, CancellationToken ct)
    {
        if (sessionId == Guid.Empty || restaurantId == Guid.Empty)
        {
            throw new BillingException(400, "Yêu cầu mã phiên và nhà hàng hợp lệ.");
        }

        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (!await bills.LockSessionAsync(sessionId, ct))
            {
                throw new BillingException(409, "Thanh toán đang được cập nhật. Vui lòng thử lại.");
            }

            var checkout = await checkouts.GetActiveAsync(sessionId, ct);
            if (checkout is null || checkout.Status != PaymentStatuses.Paid || !checkout.BillId.HasValue
                || checkout.ReleasedAt.HasValue || !checkout.PresentedAt.HasValue)
            {
                throw new BillingException(409, "Phiên bàn chưa có hóa đơn đã thanh toán hoàn tất.");
            }

            var bill = await bills.GetForRefreshAsync(checkout.BillId.Value, ct)
                ?? throw new BillingException(409, "Không tìm thấy hóa đơn của phiên bàn.");
            var sessionIds = bill.BillLines.Select(line => line.TableSessionRef).Distinct().ToArray();
            var hash = DepartureHash(bill);
            if (bill.RestaurantId != restaurantId || sessionIds.Length != 1 || sessionIds[0] != sessionId
                || bill.Status != PaymentStatuses.Paid || !bill.PaidAt.HasValue || bill.TotalAmount <= 0
                || bill.Currency != "VND" || hash != checkout.SourceHash)
            {
                throw new BillingException(409, "Hóa đơn không khớp phiên bàn hoặc chưa hoàn tất thanh toán.");
            }

            var blocking = bill.Payments.Where(PayOsPaymentRules.IsBlocking).ToArray();
            if (blocking.Length != 1)
            {
                throw new BillingException(409, "Các giao dịch của hóa đơn cần được đối soát trước khi kết thúc phiên.");
            }

            var payment = await payments.GetDetailsAsync(blocking[0].PaymentId, ct)
                ?? throw new BillingException(409, "Không tìm thấy biên lai thanh toán.");
            if (payment.CashShift.CashRegister.RestaurantId != restaurantId
                || payment.CashShift.CashRegisterId != checkout.CashRegisterId
                || !(PayOsPaymentRules.IsVerifiedPaid(payment) || IsVerifiedCashForDeparture(payment)))
            {
                throw new BillingException(409, "Thanh toán chưa được xác minh hoặc có giao dịch cần kiểm tra.");
            }

            return new PaidSessionSettlement(sessionId, restaurantId, bill.BillId, checkout.CheckoutId,
                new DateTimeOffset(DateTime.SpecifyKind(bill.PaidAt.Value, DateTimeKind.Utc)));
        }, ct);
    }

    public async Task<bool> TryClose(Guid sessionId, Guid restaurantId, CancellationToken ct)
    {
        try
        {
            var payment = await GetPaid(sessionId, restaurantId, ct);
            return await client.TryCloseAsync(payment, ct);
        }
        catch (Exception error)
        {
            logger.LogWarning("Thanh toán đã lưu; chưa xác nhận đóng phiên {SessionId}. Loại lỗi: {ErrorType}. Sẽ đối chiếu lại từ phiên đang mở.",
                sessionId, error.GetType().Name);
            return false;
        }
    }

    private static string DepartureHash(Bill bill)
    {
        try
        {
            return CheckoutHash.Compute(bill.BillLines.Select(line =>
            {
                var item = JsonSerializer.Deserialize<BillItemSnapshot>(line.ItemSnapshot) ?? throw new JsonException();
                return new CheckoutHashLine(line.OrderItemRef, item.ItemId, line.Quantity, item.BaseUnitPrice,
                    item.Options.Select(option => new CheckoutHashOption(option.OptionId, option.Quantity, option.ExtraPrice)).ToArray());
            }));
        }
        catch (JsonException)
        {
            throw new BillingException(409, "Không thể xác minh bản chụp hóa đơn đã lưu.");
        }
    }

    private bool IsVerifiedCashForDeparture(Payment payment)
    {
        var sale = payment.CashMovements.ToArray();
        var handover = payment.CashHandover;
        return payment.MethodCode == settings.Value.MethodCode && payment.Status == PaymentStatuses.Paid
            && payment.VerificationSource == PaymentStatuses.CashierVerification
            && payment.ProviderOrderCode is null && payment.ProviderTransactionId is null && payment.Refunds.Count == 0
            && payment.PaidAt.HasValue && payment.PaidAt == payment.Bill.PaidAt && payment.Bill.Status == PaymentStatuses.Paid
            && payment.Amount == payment.Bill.TotalAmount && payment.Amount > 0 && decimal.Truncate(payment.Amount) == payment.Amount
            && payment.ConfirmedByRef == payment.CashShift.CashierUserRef && payment.ConfirmedByRef != Guid.Empty
            && payment.CashReceived is decimal received && payment.ChangeAmount is decimal change
            && received >= payment.Amount && change >= 0 && received - change == payment.Amount
            && decimal.Truncate(received) == received && decimal.Truncate(change) == change
            && sale.Length == 1 && sale[0].Type == PaymentStatuses.SaleMovement && sale[0].SignedAmount == payment.Amount
            && sale[0].CashShiftId == payment.CashShiftId && sale[0].PostedAt == payment.PaidAt
            && (handover is null || (handover.Status == CashHandoverStatuses.Confirmed && handover.WaiterUserRef != Guid.Empty
                && handover.CashierUserRef == payment.ConfirmedByRef && handover.ConfirmedAt == payment.PaidAt
                && handover.ConfirmedAmount == received && handover.CollectedAt <= handover.ConfirmedAt));
    }
}
