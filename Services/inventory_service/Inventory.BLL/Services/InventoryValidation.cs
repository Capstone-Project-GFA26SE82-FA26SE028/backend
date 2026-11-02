using Inventory.Common;

namespace Inventory.BLL.Services;

public static class InventoryValidation
{
    public static void Page(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
        {
            throw new InventoryException(400, "Phân trang không hợp lệ.");
        }
    }

    public static bool Quantity(decimal value)
    {
        return value >= 0 && value <= 999999999999.999999m && decimal.Round(value, 6) == value;
    }

    public static void Id(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new InventoryException(400, "Mã ID không được để trống.");
        }
    }
}
