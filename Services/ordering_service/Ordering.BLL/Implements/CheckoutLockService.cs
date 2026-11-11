using Ordering.BLL.Interfaces;
using Ordering.Common.DTOs;
using Ordering.Common.Exceptions;
using Ordering.DAL.Interfaces;
using Ordering.DAL.Models;
using Shared.Common.Billing;

namespace Ordering.BLL.Implements;

public sealed class CheckoutLockService(ICheckoutLockRepository locks, IOrderRepository orders,
    IOrderingTransaction transaction, ICheckoutAccess access, ICheckoutReleaseAuthorization releaseAuthorization,
    TimeProvider clock) : ICheckoutLockService
{
    public Task Verify(Guid sessionId, string guestToken, CancellationToken ct)
    {
        return access.RequireAsync(sessionId, guestToken, false, ct);
    }

    public async Task<CheckoutLockDTO> Begin(Guid sessionId, Guid id, string guestToken, CancellationToken ct)
    {
        await access.RequireRequesterAsync(sessionId, guestToken, ct);
        return await transaction.ExecuteAsync(async () =>
        {
            await locks.LockSessionAsync(sessionId, ct);
            var value = await locks.GetByIdAsync(id, ct);
            if (value is not null && value.TableSessionRef != sessionId)
            {
                throw new OrderingException(409, "Mã thanh toán này thuộc về phiên phục vụ khác.");
            }

            if (value is null)
            {
                if (await locks.GetActiveAsync(sessionId, ct) is not null)
                {
                    throw new OrderingException(409, "Phiên này đã có yêu cầu thanh toán đang hoạt động. Vui lòng tiếp tục thanh toán đó.");
                }

                value = new CheckoutLock
                {
                    CheckoutId = id,
                    TableSessionRef = sessionId,
                    CreatedAt = clock.GetUtcNow().UtcDateTime,
                    Status = "WaitingConfirmation"
                };
                locks.Add(value);
            }

            if (value.ReleasedAt.HasValue || value.Status == "Frozen")
            {
                return Map(value);
            }

            var source = await orders.GetForBillingAsync(sessionId, ct);
            if (source.All(order => order.Status != "PendingConfirmation"))
            {
                value.SourceHash = Hash(source);
                value.Status = "Frozen";
            }

            return Map(value);
        }, ct);
    }

    public async Task<CheckoutLockDTO> Release(Guid sessionId, Guid id, CancellationToken ct)
    {
        await access.RequireAsync(sessionId, string.Empty, true, ct);
        await releaseAuthorization.RequireAsync(sessionId, id, ct);
        return await transaction.ExecuteAsync(async () =>
        {
            await locks.LockSessionAsync(sessionId, ct);
            var value = await locks.GetByIdAsync(id, ct);
            if (value is not null && value.TableSessionRef != sessionId)
            {
                throw new OrderingException(409, "Yêu cầu thanh toán không thuộc về phiên phục vụ này.");
            }

            if (value is null)
            {
                value = new CheckoutLock
                {
                    CheckoutId = id,
                    TableSessionRef = sessionId,
                    CreatedAt = clock.GetUtcNow().UtcDateTime,
                    Status = "Released"
                };
                locks.Add(value);
            }

            value.Status = "Released";
            value.ReleasedAt ??= clock.GetUtcNow().UtcDateTime;
            return Map(value);
        }, ct);
    }

    public async Task<CheckoutLockDTO> Bind(Guid sessionId, Guid id, Guid billId, string hash, CancellationToken ct)
    {
        await access.RequireAsync(sessionId, string.Empty, true, ct);
        return await transaction.ExecuteAsync(async () =>
        {
            await locks.LockSessionAsync(sessionId, ct);
            var value = await locks.GetByIdAsync(id, ct);
            if (value is null || value.TableSessionRef != sessionId || value.Status != "Frozen"
                || value.ReleasedAt.HasValue || value.SourceHash != hash
                || (value.BillIdRef.HasValue && value.BillIdRef != billId))
            {
                throw new OrderingException(409, "Yêu cầu thanh toán chưa được khóa với bản chụp hóa đơn này.");
            }

            value.BillIdRef = billId;
            return Map(value);
        }, ct);
    }

    private static string Hash(IEnumerable<Order> orders)
    {
        var items = orders.Where(order => order.Status is "Confirmed" or "Ready" or "Served")
            .SelectMany(order => order.OrderItems).ToArray();
        if (items.Any(item => item.CancelledQuantity < 0 || item.CancelledQuantity > item.Quantity))
        {
            throw new OrderingException(409, "Món ăn có số lượng hủy không hợp lệ.");
        }

        return CheckoutHash.Compute(items.Where(item => item.Status != "Cancelled").Select(item =>
            new CheckoutHashLine(item.OrderItemId, item.ItemRef, item.Quantity - item.CancelledQuantity,
                item.UnitPriceSnapshot, item.OrderItemOptions.Select(option =>
                    new CheckoutHashOption(option.OptionRef, option.Quantity, option.ExtraPriceSnapshot)).ToArray())));
    }

    private static CheckoutLockDTO Map(CheckoutLock value)
    {
        return new CheckoutLockDTO
        {
            CheckoutId = value.CheckoutId,
            TableSessionId = value.TableSessionRef,
            Status = value.Status,
            SourceHash = value.SourceHash
        };
    }
}
