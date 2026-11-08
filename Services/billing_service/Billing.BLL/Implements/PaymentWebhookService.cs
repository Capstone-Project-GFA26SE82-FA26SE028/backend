using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Billing.BLL.Interfaces;
using Billing.Common.Constants;
using Billing.Common.Exceptions;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Shared.Persistence.Interfaces;

namespace Billing.BLL.Implements;

public sealed class PaymentWebhookService(IPaymentRepository payments, IPaymentWebhookRepository webhooks,
    IBillRepository bills, ICashRegisterRepository registers, ICashShiftRepository shifts, IUnitOfWork unitOfWork,
    IPayOsClient provider, IPayOsPaymentService paymentService, TimeProvider clock) : IPaymentWebhookService
{
    public async Task Process(string payload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payload) || System.Text.Encoding.UTF8.GetByteCount(payload) > 65536)
        {
            throw new BillingException(400, "Yêu cầu dữ liệu webhook có kích thước tối đa 64 KiB.");
        }

        var data = provider.VerifyWebhook(payload);
        var matches = await payments.GetByProviderCodeAsync(data.OrderCode.ToString(CultureInfo.InvariantCulture), ct);
        var target = matches.Count == 1 && PayOsPaymentRules.IsPayOs(matches[0]) ? matches[0] : null;
        payments.ResetProviderTracking();
        var shouldSync = await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (target is not null && !await payments.LockProviderWorkAsync(target.PaymentId, ct))
            {
                throw new BillingException(503, "Đối soát webhook đang bận; vui lòng gửi lại.");
            }

            if (!await webhooks.LockKeyAsync(data.DedupeKey, ct))
            {
                throw new BillingException(503, "Xử lý chống trùng lặp webhook đang bận; vui lòng gửi lại.");
            }

            var prior = await webhooks.FindAsync(data.DedupeKey, ct);
            if (prior.Count > 1)
            {
                throw new BillingException(409, "Các bản ghi webhook không nhất quán và cần được kiểm tra.");
            }

            var conflict = prior.Count == 1 && (prior[0].PayloadHash != data.PayloadHash || prior[0].PaymentId != target?.PaymentId);
            if (!conflict && prior.Count == 1 && prior[0].ProcessedAt.HasValue)
            {
                return false;
            }

            Payment? payment = null;
            if (target is not null)
            {
                var bill = await bills.GetDetailsAsync(target.BillId, ct) ?? throw new BillingException(409, "Không tìm thấy hóa đơn tương ứng với webhook.");
                var sessions = bill.BillLines.Select(value => value.TableSessionRef).Distinct().ToArray();
                if (sessions.Length != 1 || !await bills.LockSessionAsync(sessions[0], ct))
                {
                    throw new BillingException(503, "Lần chốt hóa đơn tương ứng với webhook đang bận hoặc không thể xác minh.");
                }

                await registers.LockByIdAsync(target.CashShift.CashRegisterId, ct);
                await shifts.LockByIdAsync(target.CashShiftId, ct);
                await bills.GetForRefreshAsync(target.BillId, ct);
                payment = await payments.GetForProviderUpdateAsync(target.PaymentId, ct);
                if (payment is null)
                {
                    throw new BillingException(409, "Không tìm thấy khoản thanh toán tương ứng với webhook.");
                }
            }

            var needsReview = conflict;
            if (payment is not null && data.Code == "00")
            {
                needsReview |= data.Currency != "VND" || data.Amount != payment.Amount || data.Amount <= 0
                    || data.LinkId.Length is 0 or > 100
                    || (payment.ProviderPaymentLinkId is not null && payment.ProviderPaymentLinkId != data.LinkId)
                    || (payment.ProviderTransactionId is not null && payment.ProviderTransactionId != data.Reference);
                if (payment.ProviderPaymentLinkId is null && data.LinkId.Length is > 0 and <= 100)
                {
                    payment.ProviderPaymentLinkId = data.LinkId;
                }

                payment.ProviderTransactionId ??= data.Reference;
                if (needsReview || payment.Status == PayOsStates.ReviewRequired)
                {
                    payment.Status = PayOsStates.ReviewRequired;
                }
                else if (payment.Status != PaymentStatuses.Paid)
                {
                    payment.Status = PayOsStates.Pending;
                    payment.VerificationSource = PayOsStates.WebhookVerified;
                }
            }

            var key = conflict ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data.DedupeKey + ":" + data.PayloadHash))).ToLowerInvariant() : data.DedupeKey;
            var entries = conflict ? await webhooks.FindAsync(key, ct) : prior;
            if (entries.Count == 0)
            {
                var finished = target is null || needsReview || data.Code != "00";
                await webhooks.AddAsync(new PaymentWebhook
                {
                    PaymentWebhookId = Guid.NewGuid(),
                    PaymentId = target?.PaymentId,
                    Provider = PayOsStates.Provider,
                    DedupeKey = key,
                    PayloadHash = data.PayloadHash,
                    SignatureValid = true,
                    ReceivedAt = clock.GetUtcNow().UtcDateTime,
                    ProcessedAt = finished ? clock.GetUtcNow().UtcDateTime : null,
                    ProcessingStatus = target is null ? "Unmatched" : needsReview ? "ReviewRequired" : data.Code != "00" ? "Ignored" : "AwaitingProvider"
                });
            }

            return target is not null && !needsReview && data.Code == "00";
        }, ct);
        if (shouldSync && target is not null)
        {
            await paymentService.Synchronize(target.PaymentId, ct);
        }
    }
}
