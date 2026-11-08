using System.Globalization;
using Billing.Common.Constants;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.DAL.Models;

namespace Billing.BLL.Implements;

public sealed partial class PayOsPaymentService
{
    public async Task<PayOsPaymentDTO> Synchronize(Guid id, CancellationToken ct)
    {
        provider.EnsureConfigured();
        payments.ResetProviderTracking();
        return await Controlled(async () => await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            var initial = await payments.GetDetailsAsync(id, ct) ?? throw new BillingException(404, "Không tìm thấy khoản thanh toán.");
            if (!PayOsPaymentRules.IsPayOs(initial))
            {
                throw new BillingException(409, "Khoản thanh toán này không thuộc tích hợp PayOS.");
            }

            if (!await payments.LockProviderWorkAsync(id, ct))
            {
                return Map(initial);
            }

            var order = long.Parse(initial.ProviderOrderCode, CultureInfo.InvariantCulture);
            PayOsProviderResult? created = null;
            PayOsProviderResult? bank = null;
            var cancelAttempted = false;
            try
            {
                bank = await provider.GetAsync(order, ct);
            }
            catch (PayOsClientException)
            {
                if (initial.Status is PayOsStates.Creating or PayOsStates.Unknown && initial.ProviderPaymentLinkId is null
                    && initial.ExpiredAt.HasValue && initial.ExpiredAt.Value > clock.GetUtcNow().UtcDateTime)
                {
                    try
                    {
                        created = await provider.CreateAsync(new PayOsCreateCommand(order, (long)initial.Amount,
                            initial.ProviderOrderCode[^9..], new DateTimeOffset(DateTime.SpecifyKind(initial.ExpiredAt.Value, DateTimeKind.Utc))), ct);
                    }
                    catch (PayOsClientException)
                    {
                        // The same provider order code is retained after any uncertain create outcome.
                    }

                    try
                    {
                        bank = await provider.GetAsync(order, ct);
                    }
                    catch (PayOsClientException)
                    {
                        bank = null;
                    }
                }
            }

            if (bank is not null && bank.OrderCode == order && bank.Amount == initial.Amount
                && (initial.ProviderPaymentLinkId is null || initial.ProviderPaymentLinkId == bank.LinkId)
                && bank.AmountPaid == 0 && bank.AmountRemaining == bank.Amount && bank.Transactions?.Count == 0
                && bank.Status is "PENDING" or "PROCESSING" && initial.ProviderTransactionId is null
                && (initial.Status == PayOsStates.CancelRequested || initial.ExpiredAt <= clock.GetUtcNow().UtcDateTime))
            {
                cancelAttempted = true;
                try
                {
                    bank = await provider.CancelAsync(order, initial.ExpiredAt <= clock.GetUtcNow().UtcDateTime
                        ? "Yêu cầu thanh toán đã hết hạn" : "Thu ngân yêu cầu hủy", ct);
                }
                catch (PayOsClientException)
                {
                    // A timed-out cancel is not confirmation that ordering or another payment may resume.
                }
            }

            var storedBill = await bills.GetDetailsAsync(initial.BillId, ct) ?? throw new BillingException(404, "Không tìm thấy hóa đơn.");
            var sessionId = Session(storedBill);
            await LockExisting(initial, sessionId, ct);
            var payment = await payments.GetForProviderUpdateAsync(id, ct) ?? throw new BillingException(404, "Không tìm thấy khoản thanh toán.");
            if (created is not null)
            {
                if (created.OrderCode != order || created.Amount != payment.Amount
                    || (payment.ProviderPaymentLinkId is not null && payment.ProviderPaymentLinkId != created.LinkId))
                {
                    payment.Status = PayOsStates.ReviewRequired;
                }
                else
                {
                    payment.ProviderPaymentLinkId = created.LinkId;
                    payment.ProviderCheckoutUrl = created.CheckoutUrl;
                    payment.ProviderQrCode = created.QrCode;
                }
            }

            await Apply(payment, bank, sessionId, cancelAttempted, ct);
            if (payment.Status == PaymentStatuses.Paid || PayOsPaymentRules.IsVerifiedZero(payment) || payment.Status == PayOsStates.ReviewRequired)
            {
                foreach (var webhook in await webhooks.PendingAsync(id, ct))
                {
                    webhook.ProcessedAt = Stamp(clock.GetUtcNow());
                    webhook.ProcessingStatus = payment.Status == PaymentStatuses.Paid ? "Applied" : payment.Status == PayOsStates.ReviewRequired ? "ReviewRequired" : "ZeroConfirmed";
                }
            }

            var result = Map(payment);
            result.ProviderAmountPaid = bank?.AmountPaid;
            result.ProviderAmountRemaining = bank?.AmountRemaining;
            return result;
        }, ct));
    }

    private async Task Apply(Payment payment, PayOsProviderResult? bank, Guid sessionId, bool cancellationAttempted, CancellationToken ct)
    {
        if (bank is null)
        {
            if (payment.Status is not (PaymentStatuses.Paid or PayOsStates.ReviewRequired) && !PayOsPaymentRules.IsVerifiedZero(payment))
            {
                payment.Status = payment.Status == PayOsStates.CancelRequested || cancellationAttempted ? PayOsStates.CancelRequested : PayOsStates.Unknown;
            }

            return;
        }

        if (bank.OrderCode.ToString(CultureInfo.InvariantCulture) != payment.ProviderOrderCode || bank.Amount != payment.Amount
            || (payment.ProviderPaymentLinkId is not null && payment.ProviderPaymentLinkId != bank.LinkId))
        {
            payment.Status = PayOsStates.ReviewRequired;
            return;
        }

        payment.ProviderPaymentLinkId = bank.LinkId;
        if (payment.Status == PayOsStates.ReviewRequired)
        {
            return;
        }

        if (bank.AmountPaid is not long paid || bank.AmountRemaining is not long remaining || bank.Transactions is null)
        {
            payment.Status = PayOsStates.Unknown;
            return;
        }

        var terminal = bank.Status is "CANCELLED" or "EXPIRED" or "FAILED";
        if (paid == 0 && remaining == bank.Amount && bank.Transactions.Count == 0 && terminal)
        {
            if (payment.ProviderTransactionId is not null || payment.PaidAt.HasValue || payment.CashMovements.Count != 0
                || payment.CashReceived.HasValue || payment.ChangeAmount.HasValue || payment.Refunds.Count != 0 || payment.CashHandover is not null)
            {
                payment.Status = PayOsStates.ReviewRequired;
                return;
            }

            payment.Status = bank.Status == "FAILED" ? "Failed" : bank.Status == "EXPIRED" || payment.ExpiredAt <= clock.GetUtcNow().UtcDateTime ? "Expired" : "Cancelled";
            payment.VerificationSource = PayOsStates.ZeroVerified;
            return;
        }

        if (paid == 0 && remaining == bank.Amount && bank.Transactions.Count == 0 && bank.Status is "PENDING" or "PROCESSING")
        {
            if (payment.Status == PaymentStatuses.Paid || PayOsPaymentRules.IsVerifiedZero(payment))
            {
                payment.Status = PayOsStates.ReviewRequired;
            }
            else
            {
                payment.Status = payment.Status == PayOsStates.CancelRequested || cancellationAttempted ? PayOsStates.CancelRequested : PayOsStates.Pending;
            }

            return;
        }

        var reference = bank.Transactions.Count == 1 ? bank.Transactions[0].Reference : null;
        var validReference = !string.IsNullOrWhiteSpace(reference) && reference.Length <= 255 && reference.All(value => value is >= ' ' and <= '~');
        if (validReference && payment.ProviderTransactionId is null)
        {
            payment.ProviderTransactionId = reference;
        }

        if (paid != payment.Amount || remaining != 0 || terminal || bank.Transactions.Count != 1
            || bank.Transactions[0].Amount != paid || !validReference || payment.ProviderTransactionId != reference)
        {
            payment.Status = PayOsStates.ReviewRequired;
            payment.VerificationSource = PayOsStates.QueryVerified;
            return;
        }

        if (bank.Status is "PENDING" or "PROCESSING")
        {
            payment.Status = payment.PaidAt.HasValue ? PayOsStates.ReviewRequired : PayOsStates.Pending;
            return;
        }

        if (bank.Status != "PAID")
        {
            payment.Status = PayOsStates.ReviewRequired;
            return;
        }

        if (payment.Status == PaymentStatuses.Paid && payment.PaidAt.HasValue && payment.VerificationSource == PayOsStates.QueryVerified
            && payment.Bill.Status == PaymentStatuses.Paid && payment.Bill.PaidAt == payment.PaidAt && payment.Bill.TotalAmount == payment.Amount)
        {
            return;
        }

        var checkout = await checkouts.GetActiveAsync(sessionId, ct);
        if (payment.Bill.Status != "Unpaid" || payment.Bill.PaidAt.HasValue || payment.Bill.TotalAmount != payment.Amount
            || payment.Bill.Currency != "VND" || payment.Bill.RestaurantId != payment.CashShift.CashRegister.RestaurantId
            || payment.CashShift.Status != CashShiftStatuses.Active || payment.CashShift.ClosedAt.HasValue
            || payment.CashReceived.HasValue || payment.ChangeAmount.HasValue || payment.CashMovements.Count != 0
            || payment.Refunds.Count != 0 || payment.CashHandover is not null
            || payment.Bill.Payments.Any(value => value.PaymentId != payment.PaymentId && PayOsPaymentRules.IsBlocking(value))
            || checkout?.BillId != payment.BillId || checkout.Status != "Ready" || checkout.SourceHash != Hash(payment.Bill))
        {
            payment.Status = PayOsStates.ReviewRequired;
            payment.VerificationSource = PayOsStates.QueryVerified;
            return;
        }

        if (!await payments.LockBankReferenceAsync(reference!, ct))
        {
            throw new BillingException(409, "Mã tham chiếu ngân hàng đang được đối soát. Vui lòng thử lại với cùng khoản thanh toán.");
        }

        if (await payments.BankReferenceUsedAsync(reference!, payment.PaymentId, ct))
        {
            payment.Status = PayOsStates.ReviewRequired;
            payment.VerificationSource = PayOsStates.QueryVerified;
            return;
        }

        payment.Status = PaymentStatuses.Paid;
        payment.VerificationSource = PayOsStates.QueryVerified;
        payment.PaidAt = Stamp(clock.GetUtcNow());
        payment.ConfirmedByRef = null;
        payment.Bill.Status = PaymentStatuses.Paid;
        payment.Bill.PaidAt = payment.PaidAt;
        var writable = await checkouts.GetByIdAsync(checkout.CheckoutId, ct) ?? throw new BillingException(409, "Không còn tìm thấy lần chốt hóa đơn.");
        writable.Status = PaymentStatuses.Paid;
    }
}
