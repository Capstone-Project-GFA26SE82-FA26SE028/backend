using System.Security.Cryptography;
using System.Text;
using Ordering.BLL.Interfaces;
using Ordering.Common.DTOs;
using Ordering.Common.Exceptions;
using Ordering.DAL.Models;

namespace Ordering.BLL.Implements;

public sealed partial class OrderService
{
    public async Task<(OrderDTO Order, bool Created)> CreateTakeaway(
        Guid restaurantId, string key, SubmitTakeawayDTO request, CancellationToken ct)
    {
        var actor = await takeawayAccess.GetActorAsync(restaurantId, ct);
        if (!actor.IsCustomer)
        {
            throw new OrderingException(403, "Chỉ khách hàng đã đăng nhập mới được đặt món mang về trực tuyến.");
        }

        if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || key.Any(char.IsControl) || key != key.Trim()
            || request.CartId == Guid.Empty || string.IsNullOrWhiteSpace(request.CartVersion)
            || string.IsNullOrWhiteSpace(request.PickupName) || request.PickupName.Length > 100
            || request.PickupPhone.Length is < 8 or > 20 || request.PickupPhone.Any(c => !char.IsAsciiDigit(c) && c != '+'))
        {
            throw new OrderingException(400, "Vui lòng cung cấp mã chống trùng yêu cầu, phiên bản giỏ hàng, tên và số điện thoại người nhận hợp lệ.");
        }

        var scopedKey = $"online:{actor.UserId:N}:{restaurantId:N}:{OrderingRules.Hash(key)}";
        var hash = OrderingRules.Hash(new
        {
            request.CartId,
            request.CartVersion,
            Name = request.PickupName.Trim(),
            request.PickupPhone
        });
        var result = await transaction.ExecuteAsync(async () =>
        {
            var existing = await orders.GetByKeyAsync(scopedKey, ct);
            if (existing is not null)
            {
                if (existing.RequestHash != hash)
                {
                    throw new OrderingException(409, "Mã chống trùng yêu cầu đã được sử dụng với nội dung khác.");
                }

                return (Order: existing, Created: false);
            }

            var now = clock.GetUtcNow();
            var cart = await carts.GetByIdAsync(request.CartId, ct);
            if (cart is null || cart.CustomerUserRef != actor.UserId || cart.RestaurantRef != restaurantId
                || cart.TableSessionRef.HasValue || cart.ExpiredAt <= now.UtcDateTime)
            {
                throw new OrderingException(404, "Không tìm thấy giỏ hàng của khách hàng.");
            }

            if (OrderingRules.CartVersion(cart) != request.CartVersion)
            {
                throw new OrderingException(409, "Giỏ hàng đã thay đổi. Vui lòng tải lại và gửi lại yêu cầu.");
            }

            var lines = OrderingRules.Selections(cart);
            OrderingRules.ValidateLines(lines, false);
            var quote = await QuoteTakeaway(restaurantId, actor.UserId, lines, ct);
            var total = quote.Items.Sum(item => item.Quantity * (item.UnitPrice
                + item.Options.Sum(option => option.Quantity * option.ExtraPrice)));
            if (total <= 0 || total > 1000000000 || decimal.Truncate(total) != total)
            {
                throw new OrderingException(409, "Tổng tiền thanh toán trực tuyến phải là số nguyên dương theo VND và không vượt quá 1 tỷ đồng.");
            }

            var order = new Order
            {
                OrderId = Guid.NewGuid(),
                RestaurantRef = restaurantId,
                CustomerUserRef = actor.UserId,
                Channel = "Takeaway",
                CreatedAt = now.UtcDateTime,
                Status = "PendingPayment",
                PaymentStatus = "Pending",
                IdempotencyKey = scopedKey,
                RequestHash = hash,
                PickupName = request.PickupName.Trim(),
                PickupPhone = request.PickupPhone,
                PickupCode = RandomNumberGenerator.GetInt32(1000, 10000).ToString()
            };
            order.OrderNumber = $"TA-{order.OrderId.ToString("N")[..6].ToUpperInvariant()}";
            for (var index = 0; index < quote.Items.Count; index++)
            {
                var snapshot = quote.Items[index];
                order.OrderItems.Add(new OrderItem
                {
                    OrderItemId = Guid.NewGuid(),
                    OrderId = order.OrderId,
                    ItemRef = snapshot.ItemId,
                    RecipeVersionRef = snapshot.RecipeVersionId,
                    NameSnapshot = snapshot.Name,
                    UnitPriceSnapshot = snapshot.UnitPrice,
                    Quantity = snapshot.Quantity,
                    Note = lines[index].Note?.Trim(),
                    Status = "PendingPayment",
                    OrderItemOptions = snapshot.Options.Select(option => new OrderItemOption
                    {
                        Id = Guid.NewGuid(),
                        OptionRef = option.OptionId,
                        NameSnapshot = option.Name,
                        ExtraPriceSnapshot = option.ExtraPrice,
                        Quantity = option.Quantity
                    }).ToList()
                });
            }

            orders.Create(order);
            carts.ReplaceItems(cart, []);
            return (Order: order, Created: true);
        }, ct);

        // Persist order/key before contacting PayOS; timeouts retry the same order and provider code.
        var current = await SynchronizeTakeaway(restaurantId, result.Order.OrderId, actor, "Create", ct);
        return (MapTakeaway(current, actor), result.Created);
    }

    public async Task<OrderDTO> GetTakeawayById(Guid restaurantId, Guid id, CancellationToken ct)
    {
        var actor = await takeawayAccess.GetActorAsync(restaurantId, ct);
        var order = await SynchronizeTakeaway(restaurantId, id, actor, "Refresh", ct);
        return MapTakeaway(order, actor);
    }

    public async Task<List<OrderDTO>> GetAllTakeaway(Guid restaurantId, string? status, int page, int pageSize, CancellationToken ct)
    {
        var actor = await takeawayAccess.GetActorAsync(restaurantId, ct);
        ValidateTakeawayList(status, page, pageSize);
        var rows = await transaction.ExecuteAsync(() => orders.GetTakeawayAsync(restaurantId,
            actor.IsCustomer ? actor.UserId : null, status, page, pageSize, ct), ct);
        return rows.Select(order => MapTakeaway(order, actor)).ToList();
    }

    public async Task<(List<OrderDTO> Items, int TotalCount)> GetMyTakeawayOrders(string? status, int page,
        int pageSize, CancellationToken ct)
    {
        var customerId = await takeawayAccess.RequireCustomerAsync(ct);
        ValidateTakeawayList(status, page, pageSize);
        var (ordersPage, totalCount) = await transaction.ExecuteAsync(
            () => orders.GetCustomerTakeawayAsync(customerId, status, page, pageSize, ct), ct);

        var items = ordersPage.Select(order =>
        {
            var result = Map(order);
            result.PaymentStatus = order.PaymentStatus;
            return result;
        }).ToList();
        return (items, totalCount);
    }

    private static void ValidateTakeawayList(string? status, int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue
            || (status is not null && status is not ("PendingPayment" or "PendingConfirmation" or "Accepted"
                or "Cooking" or "ReadyForPickup" or "Completed" or "Cancelled" or "Rejected")))
        {
            throw new OrderingException(400, "Thông tin phân trang hoặc trạng thái đơn mang về không hợp lệ.");
        }
    }

    public async Task<OrderDTO> AcceptTakeaway(Guid restaurantId, Guid id, string version, CancellationToken ct)
    {
        var actor = await takeawayAccess.GetActorAsync(restaurantId, ct);
        RequireCashier(actor);
        await SynchronizeTakeaway(restaurantId, id, actor, "Refresh", ct);
        var order = await transaction.ExecuteAsync(async () =>
        {
            var found = await FindTakeaway(restaurantId, id, actor, ct);
            if (found.Status is "Accepted" or "Cooking" or "ReadyForPickup" or "Completed")
            {
                return found;
            }

            CheckTakeawayVersion(found, version);
            if (found.Status != "PendingConfirmation" || found.PaymentStatus != "Paid" || found.CancellationRequested)
            {
                throw new OrderingException(409, "Chỉ được chấp nhận đơn đang chờ xác nhận và đã được xác minh thanh toán thành công.");
            }

            var lines = found.OrderItems.OrderBy(item => item.OrderItemId).Select(Selection).ToList();
            var quote = await QuoteTakeaway(restaurantId, actor.UserId, lines, ct);
            var stations = new Dictionary<Guid, Guid>();
            foreach (var item in quote.Items)
            {
                stations[item.ItemId] = await Station(item.KitchenStationId, ct);
            }

            var now = clock.GetUtcNow().UtcDateTime;
            foreach (var item in found.OrderItems)
            {
                var snapshot = quote.Items.First(value => value.ItemId == item.ItemRef);
                item.Status = "Pending";
                Enqueue(item, stations[item.ItemRef], actor.UserId, now, "Pending", 50, snapshot.PreparationTimeMinutes);
            }

            found.Status = "Accepted";
            found.ConfirmedAt = now;
            found.ConfirmedByRef = actor.UserId;
            return found;
        }, ct);
        return MapTakeaway(order, actor);
    }

    public Task<OrderDTO> CancelTakeaway(Guid restaurantId, Guid id, string version, CancellationToken ct)
    {
        return CancelOnline(restaurantId, id, version, false, "Khách hàng hủy đơn trước khi được chấp nhận", ct);
    }

    public Task<OrderDTO> RejectTakeaway(Guid restaurantId, Guid id, string version, string reason, CancellationToken ct)
    {
        return CancelOnline(restaurantId, id, version, true, reason, ct);
    }

    private async Task<OrderDTO> CancelOnline(Guid restaurantId, Guid id, string version, bool reject, string reason, CancellationToken ct)
    {
        var actor = await takeawayAccess.GetActorAsync(restaurantId, ct);
        if (reject)
        {
            RequireCashier(actor);
        }
        else if (!actor.IsCustomer)
        {
            throw new OrderingException(403, "Chỉ khách hàng sở hữu đơn hàng mới được hủy đơn trực tuyến.");
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000)
        {
            throw new OrderingException(400, "Vui lòng nhập lý do hủy hoặc từ chối đơn hàng.");
        }

        await transaction.ExecuteAsync(async () =>
        {
            var order = await FindTakeaway(restaurantId, id, actor, ct);
            if (order.CancellationRequested)
            {
                if (order.RejectionRequested != reject || order.CancellationReason != reason.Trim())
                {
                    throw new OrderingException(409, "Một yêu cầu hủy khác đang được xử lý.");
                }

                return true;
            }

            CheckTakeawayVersion(order, version);
            if (reject ? order.Status != "PendingConfirmation" || order.PaymentStatus != "Paid"
                : order.Status is not ("PendingPayment" or "PendingConfirmation"))
            {
                throw new OrderingException(409, "Không được hủy đơn sau khi nhân viên phục vụ đã chấp nhận.");
            }

            order.CancellationRequested = true;
            order.RejectionRequested = reject;
            order.CancellationReason = reason.Trim();
            return true;
        }, ct);
        var current = await SynchronizeTakeaway(restaurantId, id, actor, "Cancel", ct);
        return MapTakeaway(current, actor);
    }

    public async Task<OrderDTO> CollectTakeaway(Guid restaurantId, Guid id, string version,
        string pickupCode, bool handoverConfirmed, CancellationToken ct)
    {
        var actor = await takeawayAccess.GetActorAsync(restaurantId, ct);
        RequireCashier(actor);
        var order = await transaction.ExecuteAsync(async () =>
        {
            var found = await FindTakeaway(restaurantId, id, actor, ct);
            if (!handoverConfirmed || string.IsNullOrWhiteSpace(pickupCode) || found.PickupCode is null
                || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(found.PickupCode),
                    Encoding.UTF8.GetBytes(pickupCode.Trim().ToUpperInvariant())))
            {
                throw new OrderingException(400, "Vui lòng cung cấp mã nhận món hợp lệ và xác nhận đã bàn giao món cho khách.");
            }

            if (found.Status == "Completed")
            {
                return found;
            }

            CheckTakeawayVersion(found, version);
            if (found.Status != "ReadyForPickup" || found.PaymentStatus != "Paid"
                || found.OrderItems.Any(item => item.Status != "Ready"))
            {
                throw new OrderingException(409, "Đơn hàng phải được thanh toán và toàn bộ món phải sẵn sàng để nhận.");
            }

            var now = clock.GetUtcNow().UtcDateTime;
            foreach (var item in found.OrderItems)
            {
                item.Status = "Served";
                foreach (var task in item.KitchenTasks.Where(task => task.Status == "Ready"))
                {
                    task.Status = "Served";
                    task.Events.Add(new Event
                    {
                        EventId = Guid.NewGuid(),
                        KitchenTaskId = task.KitchenTaskId,
                        FromStatus = "Ready",
                        ToStatus = "Served",
                        ActorUserRef = actor.UserId,
                        OccurredAt = now
                    });
                }
            }

            found.Status = "Completed";
            found.CompletedAt = now;
            return found;
        }, ct);
        return MapTakeaway(order, actor);
    }

    public async Task<OrderDTO> VerifyTakeawayRefund(Guid restaurantId, Guid id, string reference, string evidence, CancellationToken ct)
    {
        var actor = await takeawayAccess.GetActorAsync(restaurantId, ct);
        if (actor.RoleId != "cashier")
        {
            throw new OrderingException(403, "Yêu cầu thu ngân được phân công tại nhà hàng.");
        }

        var found = await transaction.ExecuteAsync(() => FindTakeaway(restaurantId, id, actor, ct), ct);
        if (found.Status is not ("Cancelled" or "Rejected"))
        {
            throw new OrderingException(409, "Chỉ được hoàn tiền cho đơn hàng đã hủy hoặc bị từ chối.");
        }

        if (string.IsNullOrEmpty(found.RefundAccountNumber))
        {
            throw new OrderingException(409, "Khách hàng chưa cung cấp tài khoản nhận hoàn tiền.");
        }

        await takeawayPayments.ExecuteAsync(found, "VerifyRefund", "", reference, evidence, ct);
        return MapTakeaway(await SynchronizeTakeaway(restaurantId, id, actor, "Refresh", ct), actor);
    }

    public async Task ReconcileTakeaway(CancellationToken ct)
    {
        var pending = await orders.GetOnlinePendingAsync(ct);
        foreach (var order in pending)
        {
            try
            {
                await SynchronizeTakeaway(order.RestaurantRef!.Value, order.OrderId,
                    new TakeawayActor(order.CustomerUserRef!.Value, true, "customer"), "Refresh", ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Each order remains pending and is retried; no unverified settlement is promoted.
            }
        }
    }

    private async Task<Order> SynchronizeTakeaway(Guid restaurantId, Guid id, TakeawayActor actor, string action, CancellationToken ct)
    {
        // Keep the order lock across payment synchronization so acceptance/cancellation cannot race locally.
        return await transaction.ExecuteAsync(async () =>
        {
            var order = await FindTakeaway(restaurantId, id, actor, ct);
            if (order.CancellationRequested)
            {
                action = order.RejectionRequested ? "Refund" : "Cancel";
            }
            else if (order.Status != "PendingPayment")
            {
                return order;
            }

            if (action == "Create" && order.Status == "PendingPayment")
            {
                var quote = await QuoteTakeaway(restaurantId, actor.UserId,
                    order.OrderItems.OrderBy(item => item.OrderItemId).Select(Selection).ToList(), ct);
                var quotedTotal = quote.Items.Sum(item => item.Quantity * (item.UnitPrice
                    + item.Options.Sum(option => option.Quantity * option.ExtraPrice)));
                if (quotedTotal != Map(order).TotalAmount)
                {
                    throw new OrderingException(409, "Giá đã thay đổi trước khi tạo thanh toán. Vui lòng hủy và gửi giỏ hàng mới.");
                }
            }

            var payment = await takeawayPayments.ExecuteAsync(order, action, order.CancellationReason ?? "", "", "", ct);
            order.PaymentStatus = payment.Status;
            order.CheckoutUrl = payment.CheckoutUrl!;
            order.RefundedAt = payment.RefundedAt?.UtcDateTime;
            order.RefundReference = payment.RefundReference!;
            if (order.CancellationRequested && payment.Status is "Cancelled" or "RefundPending" or "Refunded")
            {
                order.Status = order.RejectionRequested ? "Rejected" : "Cancelled";
                foreach (var item in order.OrderItems)
                {
                    item.Status = "Cancelled";
                }
            }
            else if (payment.Status == "Paid" && order.Status == "PendingPayment")
            {
                order.Status = "PendingConfirmation";
                foreach (var item in order.OrderItems)
                {
                    item.Status = "PendingConfirmation";
                }
            }
            else if (payment.Status == "Cancelled" && order.Status == "PendingPayment")
            {
                order.Status = "Cancelled";
                foreach (var item in order.OrderItems)
                {
                    item.Status = "Cancelled";
                }
            }

            return order;
        }, ct);
    }

    private async Task<Order> FindTakeaway(Guid restaurantId, Guid id, TakeawayActor actor, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(id, ct);
        if (order is null || order.Channel != "Takeaway" || order.TableSessionRef.HasValue
            || order.RestaurantRef != restaurantId || !order.CustomerUserRef.HasValue
            || (actor.IsCustomer && order.CustomerUserRef != actor.UserId))
        {
            throw new OrderingException(404, "Không tìm thấy đơn hàng trực tuyến.");
        }

        return order;
    }

    private async Task<QuoteDTO> QuoteTakeaway(Guid restaurantId, Guid userId, List<SelectionDTO> lines, CancellationToken ct)
    {
        var quote = await catalog.ValidateAsync(new OrderingActor(Guid.Empty, null, userId, null, null)
        {
            Channel = "Takeaway",
            RestaurantId = restaurantId
        }, "", lines, ct);
        if (!quote.IsValid)
        {
            throw new OrderingException(409, quote.Message);
        }

        return quote;
    }

    private static SelectionDTO Selection(OrderItem item)
    {
        return new SelectionDTO
        {
            ItemId = item.ItemRef,
            Quantity = item.Quantity,
            Options = item.OrderItemOptions.Select(option => new OptionSelectionDTO
            {
                OptionId = option.OptionRef,
                Quantity = option.Quantity
            }).ToList()
        };
    }

    private static void RequireCashier(TakeawayActor actor)
    {
        if (actor.IsCustomer || actor.RoleId != "cashier")
        {
            throw new OrderingException(403, "Yêu cầu thu ngân được phân công tại nhà hàng.");
        }
    }

    private void CheckTakeawayVersion(Order order, string version)
    {
        var expected = new byte[8];
        if (version is null || !Convert.TryFromBase64String(version, expected, out var size) || size != 8)
        {
            throw new OrderingException(400, "Vui lòng cung cấp phiên bản đơn hàng hợp lệ.");
        }

        if (!order.Version.SequenceEqual(expected))
        {
            throw new OrderingException(409, "Đơn hàng đã thay đổi. Vui lòng tải lại trước khi cập nhật.");
        }

        orders.SetOriginalVersion(order, expected);
    }

    private static OrderDTO MapTakeaway(Order order, TakeawayActor actor)
    {
        var result = Map(order);
        result.Source = "Customer";
        result.PickupName = order.PickupName;
        result.PickupPhone = order.PickupPhone;
        result.PickupCode = actor.IsCustomer && order.Status is not ("Cancelled" or "Rejected") ? order.PickupCode : null;
        result.PaymentStatus = order.PaymentStatus;
        result.CheckoutUrl = actor.IsCustomer && order.Status == "PendingPayment" ? order.CheckoutUrl : null;
        result.CancellationReason = order.CancellationReason;
        result.CancellationRequested = order.CancellationRequested;
        result.ReadyForPickupAt = order.ReadyForPickupAt.HasValue ? OrderingRules.Utc(order.ReadyForPickupAt.Value) : null;
        result.RefundedAt = order.RefundedAt.HasValue ? OrderingRules.Utc(order.RefundedAt.Value) : null;
        result.RefundReference = order.RefundReference;
        return result;
    }
}
