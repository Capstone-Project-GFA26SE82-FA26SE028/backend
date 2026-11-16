using Ordering.Common.DTOs;
using Ordering.Common.Exceptions;
using Ordering.DAL.Models;

namespace Ordering.BLL.Implements;

public sealed partial class CartService
{
    public async Task<CartDTO> Takeaway(Guid restaurantId, Guid? cartId, string operation, CartWriteDTO? request, CancellationToken ct)
    {
        var user = await takeawayAccess.GetActorAsync(restaurantId, ct);
        if (!user.IsCustomer)
        {
            throw new OrderingException(403, "Giỏ hàng trực tuyến yêu cầu tài khoản khách hàng.");
        }

        var actor = new OrderingActor(Guid.Empty, null, user.UserId, null, null)
        {
            Channel = "Takeaway",
            RestaurantId = restaurantId
        };
        return await transaction.ExecuteAsync(async () =>
        {
            var cart = cartId.HasValue ? await carts.GetByIdAsync(cartId.Value, ct)
                : await carts.GetForCustomerAsync(restaurantId, user.UserId, clock.GetUtcNow().UtcDateTime, ct);
            if (cart is null && operation == "Create" && !cartId.HasValue)
            {
                cart = new Cart
                {
                    CartId = Guid.NewGuid(),
                    CustomerUserRef = user.UserId,
                    RestaurantRef = restaurantId,
                    ExpiredAt = clock.GetUtcNow().AddHours(24).UtcDateTime
                };
                carts.Create(cart);
            }

            if (cart is null || cart.CustomerUserRef != user.UserId || cart.RestaurantRef != restaurantId
                || cart.TableSessionRef.HasValue || cart.ExpiredAt <= clock.GetUtcNow().UtcDateTime)
            {
                throw new OrderingException(404, "Không tìm thấy giỏ hàng của khách hàng hoặc giỏ hàng đã hết hạn.");
            }

            if (operation is "Update" or "Delete")
            {
                if (string.IsNullOrEmpty(request?.Version))
                {
                    throw new OrderingException(400, "Vui lòng cung cấp phiên bản giỏ hàng.");
                }

                if (request.Version != OrderingRules.CartVersion(cart))
                {
                    throw new OrderingException(409, "Giỏ hàng đã thay đổi. Vui lòng tải lại trước khi chỉnh sửa.");
                }

                var lines = operation == "Delete" ? new List<SelectionDTO>() : request.Items;
                OrderingRules.ValidateLines(lines, true);
                var quote = await Quote(actor, "", lines, ct);
                if (!quote.IsValid)
                {
                    throw new OrderingException(409, quote.Message);
                }

                carts.ReplaceItems(cart, lines.Select(line => new CartItem
                {
                    CartItemId = Guid.NewGuid(),
                    CartId = cart.CartId,
                    ItemRef = line.ItemId,
                    Quantity = line.Quantity,
                    Note = line.Note?.Trim(),
                    CartItemOptions = line.Options.Select(option => new CartItemOption
                    {
                        CartItemOptionId = Guid.NewGuid(),
                        OptionRef = option.OptionId,
                        Quantity = option.Quantity
                    }).ToList()
                }).ToList());
            }

            return await Map(cart, actor, "", ct);
        }, ct);
    }
}
