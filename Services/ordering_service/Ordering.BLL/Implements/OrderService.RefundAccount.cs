using Ordering.Common.DTOs;
using Ordering.Common.Exceptions;
using Ordering.DAL.Models;

namespace Ordering.BLL.Implements;

public sealed partial class OrderService
{
    public async Task<RefundAccountDTO> GetRefundAccount(Guid restaurantId, Guid id, CancellationToken ct)
    {
        var actor = await takeawayAccess.GetActorAsync(restaurantId, ct);
        if (!actor.IsCustomer && actor.RoleId != "cashier")
        {
            throw new OrderingException(403, "Chỉ chủ đơn hoặc thu ngân của nhà hàng được xem tài khoản nhận hoàn tiền.");
        }

        return MapRefundAccount(await FindTakeaway(restaurantId, id, actor, ct));
    }

    public async Task<RefundAccountDTO> SaveRefundAccount(Guid restaurantId, Guid id, RefundAccountDTO request, CancellationToken ct)
    {
        var actor = await takeawayAccess.GetActorAsync(restaurantId, ct);
        if (!actor.IsCustomer)
        {
            throw new OrderingException(403, "Chỉ chủ đơn được cung cấp tài khoản nhận hoàn tiền.");
        }

        var bank = request.BankName?.Trim() ?? "";
        var number = request.AccountNumber?.Trim() ?? "";
        var holder = request.AccountHolder?.Trim() ?? "";
        if (bank.Length is < 2 or > 100 || bank.Any(char.IsControl)
            || number.Length is < 1 or > 34 || !number.All(char.IsAsciiDigit)
            || holder.Length is < 2 or > 150 || holder.Any(char.IsControl))
        {
            throw new OrderingException(400, "Vui lòng nhập ngân hàng, số tài khoản dạng số (tối đa 34 chữ số) và tên chủ tài khoản hợp lệ.");
        }

        return await transaction.ExecuteAsync(async () =>
        {
            var order = await FindTakeaway(restaurantId, id, actor, ct);
            if (order.Status is not ("Cancelled" or "Rejected") || order.PaymentStatus != "RefundPending")
            {
                throw new OrderingException(409, "Chỉ được gửi tài khoản cho đơn đang chờ hoàn tiền.");
            }

            if (order.RefundAccountNumber is not null)
            {
                if (order.RefundBankName != bank || order.RefundAccountNumber != number || order.RefundAccountHolder != holder)
                {
                    throw new OrderingException(409, "Thông tin đã gửi cho thu ngân, không thể tự thay đổi để tránh chuyển tiền sai tài khoản. Vui lòng liên hệ nhà hàng.");
                }

                return MapRefundAccount(order);
            }

            order.RefundBankName = bank;
            order.RefundAccountNumber = number;
            order.RefundAccountHolder = holder;
            return MapRefundAccount(order);
        }, ct);
    }

    private static RefundAccountDTO MapRefundAccount(Order order)
    {
        return new RefundAccountDTO
        {
            Submitted = order.RefundAccountNumber is not null,
            BankName = order.RefundBankName ?? "",
            AccountNumber = order.RefundAccountNumber ?? "",
            AccountHolder = order.RefundAccountHolder ?? ""
        };
    }
}
