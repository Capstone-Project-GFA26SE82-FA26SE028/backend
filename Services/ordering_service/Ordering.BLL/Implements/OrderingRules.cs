using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ordering.Common.DTOs;
using Ordering.Common.Exceptions;
using Ordering.DAL.Models;

namespace Ordering.BLL.Implements;

internal static class OrderingRules
{
    public static void ValidateLines(List<SelectionDTO>? items, bool allowEmpty)
    {
        if (items is null || items.Count > 100 || (!allowEmpty && items.Count == 0))
        {
            throw new OrderingException(400, "Provide 1-100 lines, or an empty list to clear the cart.");
        }

        foreach (var line in items)
        {
            if (line is null || line.ItemId == Guid.Empty || line.Quantity is < 1 or > 1000
                || line.Note?.Length > 1000 || line.Options is null || line.Options.Count > 100)
            {
                throw new OrderingException(400, "Invalid item, quantity, note (maximum 1000 characters), or options.");
            }

            var ids = new HashSet<Guid>();
            foreach (var option in line.Options)
            {
                if (option is null || option.OptionId == Guid.Empty || option.Quantity is < 1 or > 1000 || !ids.Add(option.OptionId))
                {
                    throw new OrderingException(400, "Option IDs must be unique and quantities must be 1-1000 per serving.");
                }
            }
        }
    }

    public static string Hash(object value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    }

    public static string CartVersion(Cart cart)
    {
        return Hash(new
        {
            cart.CartId,
            cart.TableSessionRef,
            cart.GuestAccessRef,
            ExpiredAtTicks = cart.ExpiredAt.Ticks,
            Items = cart.CartItems.OrderBy(item => item.CartItemId).Select(item => new
            {
                item.CartItemId,
                item.ItemRef,
                item.Quantity,
                item.Note,
                Options = item.CartItemOptions.OrderBy(option => option.OptionRef).Select(option => new
                {
                    option.OptionRef,
                    option.Quantity
                })
            })
        });
    }

    public static void CheckCart(Cart? cart, OrderingActor actor, DateTime now, string? expected = null)
    {
        if (!actor.IsGuest)
        {
            throw new OrderingException(403, "Guest access is required for a cart. Staff can create orders directly.");
        }

        if (cart is null || cart.GuestAccessRef != actor.GuestId || cart.TableSessionRef != actor.SessionId)
        {
            throw new OrderingException(404, "Cart not found.");
        }

        if (cart.ExpiredAt <= now)
        {
            throw new OrderingException(409, "Cart has expired.");
        }

        if (expected is not null && !string.Equals(CartVersion(cart), expected, StringComparison.Ordinal))
        {
            throw new OrderingException(409, "The cart changed. Reload before saving or submitting.");
        }
    }

    public static List<SelectionDTO> Selections(Cart cart)
    {
        return cart.CartItems.OrderBy(item => item.CartItemId).Select(item => new SelectionDTO
        {
            ItemId = item.ItemRef,
            Quantity = item.Quantity,
            Note = item.Note,
            Options = item.CartItemOptions.OrderBy(option => option.OptionRef).Select(option => new OptionSelectionDTO
            {
                OptionId = option.OptionRef,
                Quantity = option.Quantity
            }).ToList()
        }).ToList();
    }

    public static DateTimeOffset Utc(DateTime value)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
