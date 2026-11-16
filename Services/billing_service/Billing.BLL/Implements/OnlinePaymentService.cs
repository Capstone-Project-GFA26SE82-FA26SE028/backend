using System.Data;
using System.Globalization;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.DAL.Interfaces;
using Billing.DAL.Models;
using Shared.Persistence.Interfaces;

namespace Billing.BLL.Implements;

public sealed class OnlinePaymentService(IOnlinePaymentRepository payments, IPaymentRepository codes,
    IUnitOfWork work, IPayOsClient provider, IBillingAccess access, TimeProvider clock) : IOnlinePaymentService
{
    public async Task<OnlinePaymentDTO> Execute(OnlinePaymentCommand command, CancellationToken ct)
    {
        if (command.OrderId == Guid.Empty || command.RestaurantId == Guid.Empty || command.CustomerUserId == Guid.Empty)
        {
            throw new BillingException(400, "Vui lòng cung cấp đơn hàng, nhà hàng và khách hàng hợp lệ.");
        }

        if (command.Action == "Create")
        {
            provider.EnsureCanCreate();
            if (command.Amount <= 0 || command.Amount > 1000000000 || decimal.Truncate(command.Amount) != command.Amount)
            {
                throw new BillingException(400, "Số tiền thanh toán phải là số nguyên dương theo VND và không vượt quá 1 tỷ đồng.");
            }

            await work.ExecuteInTransactionAsync(async () =>
            {
                var previous = await payments.GetByIdAsync(command.OrderId, ct);
                if (previous is null)
                {
                    payments.Create(new OnlinePayment
                    {
                        OrderId = command.OrderId,
                        RestaurantId = command.RestaurantId,
                        CustomerUserId = command.CustomerUserId,
                        Amount = command.Amount,
                        ProviderOrderCode = long.Parse(await codes.AllocateOrderCodeAsync(ct), CultureInfo.InvariantCulture),
                        ExpiresAt = clock.GetUtcNow().AddMinutes(15).UtcDateTime
                    });
                }
                else
                {
                    Match(previous, command);
                }

                return true;
            }, IsolationLevel.Serializable, ct);
        }

        return await work.ExecuteInTransactionAsync(async () =>
        {
            var payment = await payments.GetByIdAsync(command.OrderId, ct);
            if (payment is null)
            {
                if (command.Action == "Cancel")
                {
                    // A durable tombstone prevents a delayed Create from opening a payment after cancellation.
                    payment = new OnlinePayment
                    {
                        OrderId = command.OrderId,
                        RestaurantId = command.RestaurantId,
                        CustomerUserId = command.CustomerUserId,
                        Amount = command.Amount,
                        ProviderOrderCode = long.Parse(await codes.AllocateOrderCodeAsync(ct), CultureInfo.InvariantCulture),
                        Status = "Cancelled",
                        ExpiresAt = clock.GetUtcNow().UtcDateTime
                    };
                    payments.Create(payment);
                    return Map(payment);
                }

                throw new BillingException(404, "Không tìm thấy thanh toán trực tuyến.");
            }

            Match(payment, command);
            if (command.Action == "VerifyRefund")
            {
                var actor = await access.GetCurrentScopeAsync(payment.RestaurantId, ct);
                if (actor.IsAdministrator || actor.RoleId != "cashier" || !actor.RestaurantIds.Contains(payment.RestaurantId))
                {
                    throw new BillingException(403, "Chỉ thu ngân được phân công mới được xác minh hoàn tiền.");
                }

                if (string.IsNullOrWhiteSpace(command.Reference) || command.Reference.Length > 255
                    || command.Evidence.Length > 2048 || !Uri.TryCreate(command.Evidence, UriKind.Absolute, out var evidence)
                    || evidence.Scheme != "https")
                {
                    throw new BillingException(400, "Vui lòng cung cấp mã tham chiếu hoàn tiền ngân hàng và đường dẫn HTTPS đến minh chứng.");
                }

                if (payment.Status == "Refunded" && payment.RefundReference == command.Reference
                    && payment.RefundEvidence == command.Evidence)
                {
                    return Map(payment);
                }

                if (payment.Status != "RefundPending")
                {
                    throw new BillingException(409, "Chỉ có thể xác minh khoản hoàn tiền đang chờ xử lý.");
                }

                payment.RefundReference = command.Reference;
                payment.RefundEvidence = command.Evidence;
                payment.RefundVerifiedBy = actor.UserId;
                payment.RefundedAt = clock.GetUtcNow().UtcDateTime;
                payment.Status = "Refunded";
                return Map(payment);
            }

            if (payment.Status == "Pending")
            {
                provider.EnsureConfigured();
                PayOsProviderResult result;
                if (payment.LinkId is null && command.Action == "Create")
                {
                    try
                    {
                        result = await provider.CreateAsync(new PayOsCreateCommand(payment.ProviderOrderCode,
                            (long)payment.Amount, "Takeaway order", new DateTimeOffset(DateTime.SpecifyKind(payment.ExpiresAt, DateTimeKind.Utc))), ct);
                    }
                    catch (PayOsClientException)
                    {
                        // A create may have succeeded remotely. Reconcile the SAME provider code.
                        result = await provider.GetAsync(payment.ProviderOrderCode, ct);
                    }
                }
                else
                {
                    result = await provider.GetAsync(payment.ProviderOrderCode, ct);
                }

                if (result.Status != "PENDING" && result.Transactions is null)
                {
                    // Creation responses may omit settlement details; verify the authoritative payment.
                    result = await provider.GetAsync(payment.ProviderOrderCode, ct);
                }

                Apply(payment, result);
                if (command.Action == "Cancel" && payment.Status == "Pending")
                {
                    await provider.CancelAsync(payment.ProviderOrderCode, "Customer cancelled before acceptance", ct);
                    Apply(payment, await provider.GetAsync(payment.ProviderOrderCode, ct));
                    if (payment.Status == "Pending")
                    {
                        throw new BillingException(409, "Việc hủy thanh toán chưa được xác minh. Vui lòng thử lại cùng yêu cầu.");
                    }
                }
            }

            if (command.Action is "Cancel" or "Refund" && payment.Status == "Paid")
            {
                if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 1000)
                {
                    throw new BillingException(400, "Vui lòng nhập lý do hoàn tiền.");
                }

                payment.Status = "RefundPending";
                payment.RefundReason = command.Reason;
            }

            return Map(payment);
        }, IsolationLevel.Serializable, ct);
    }

    public async Task<bool> ProcessWebhook(string payload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payload) || System.Text.Encoding.UTF8.GetByteCount(payload) > 65536)
        {
            throw new BillingException(400, "Dữ liệu webhook không được vượt quá 64 KiB.");
        }

        var webhook = provider.VerifyWebhook(payload);
        var payment = await payments.GetByCodeAsync(webhook.OrderCode, ct);
        if (payment is null)
        {
            return false;
        }

        await Execute(new OnlinePaymentCommand(payment.OrderId, payment.RestaurantId, payment.CustomerUserId,
            payment.Amount, "Refresh"), ct);
        return true;
    }

    private static void Match(OnlinePayment payment, OnlinePaymentCommand command)
    {
        if (payment.RestaurantId != command.RestaurantId || payment.CustomerUserId != command.CustomerUserId
            || payment.Amount != command.Amount)
        {
            throw new BillingException(409, "Thông tin thanh toán không khớp với dữ liệu đã ghi nhận.");
        }
    }

    private void Apply(OnlinePayment payment, PayOsProviderResult result)
    {
        if (result.OrderCode != payment.ProviderOrderCode || result.Amount != payment.Amount
            || (payment.LinkId is not null && payment.LinkId != result.LinkId))
        {
            throw new BillingException(409, "Thông tin thanh toán từ nhà cung cấp không khớp với đơn hàng.");
        }

        payment.LinkId = result.LinkId;
        payment.CheckoutUrl = result.CheckoutUrl ?? payment.CheckoutUrl;
        if (result.Status == "PAID")
        {
            if (result.AmountPaid != payment.Amount || result.AmountRemaining != 0 || result.Transactions is null
                || result.Transactions.Count != 1 || result.Transactions[0].Amount != payment.Amount
                || string.IsNullOrWhiteSpace(result.Transactions[0].Reference))
            {
                throw new BillingException(409, "Thanh toán cần được đối soát và xác minh đã thanh toán chính xác số tiền yêu cầu.");
            }

            payment.Status = "Paid";
            payment.TransactionReference = result.Transactions[0].Reference;
            payment.PaidAt ??= clock.GetUtcNow().UtcDateTime;
        }
        else if (result.Status is "CANCELLED" or "EXPIRED" or "FAILED")
        {
            if (result.AmountPaid != 0 || result.Transactions?.Count != 0)
            {
                throw new BillingException(409, "Không thể hủy thanh toán khi khoản tiền đã nhận chưa được xác minh.");
            }

            payment.Status = "Cancelled";
        }
    }

    private static OnlinePaymentDTO Map(OnlinePayment payment)
    {
        return new OnlinePaymentDTO(payment.Status, payment.CheckoutUrl, payment.Amount,
            payment.PaidAt, payment.RefundedAt, payment.RefundReference);
    }
}
