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
            throw new OrderingException(400, "Cung cấp từ 1-100 dòng món, hoặc danh sách trống để xóa giỏ hàng.");
        }

        foreach (var line in items)
        {
            if (line is null || line.ItemId == Guid.Empty || line.Quantity is < 1 or > 1000
                || line.Note?.Length > 1000 || line.Options is null || line.Options.Count > 100)
            {
                throw new OrderingException(400, "Món ăn, số lượng, ghi chú (tối đa 1000 ký tự) hoặc tùy chọn không hợp lệ.");
            }

            var ids = new HashSet<Guid>();
            foreach (var option in line.Options)
            {
                if (option is null || option.OptionId == Guid.Empty || option.Quantity is < 1 or > 1000 || !ids.Add(option.OptionId))
                {
                    throw new OrderingException(400, "Mã tùy chọn không được trùng lặp và số lượng phải từ 1-1000 cho mỗi phần.");
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
            throw new OrderingException(403, "Yêu cầu quyền truy cập của khách đối với giỏ hàng. Nhân viên có thể tạo đơn hàng trực tiếp.");
        }

        if (cart is null || cart.GuestAccessRef != actor.GuestId || cart.TableSessionRef != actor.SessionId)
        {
            throw new OrderingException(404, "Không tìm thấy giỏ hàng.");
        }

        if (cart.ExpiredAt <= now)
        {
            throw new OrderingException(409, "Giỏ hàng đã hết hạn.");
        }

        if (expected is not null && !string.Equals(CartVersion(cart), expected, StringComparison.Ordinal))
        {
            throw new OrderingException(409, "Giỏ hàng đã thay đổi. Vui lòng tải lại trước khi lưu hoặc gửi đơn.");
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
