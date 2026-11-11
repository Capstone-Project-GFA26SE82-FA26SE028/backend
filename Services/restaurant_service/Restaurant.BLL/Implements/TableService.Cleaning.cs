using Restaurant.Common.DTOs;
using Restaurant.Common.Exceptions;

namespace Restaurant.BLL.Implements;

public sealed partial class TableService
{
    public async Task<TableDto> ConfirmClean(Guid id, string version, CancellationToken ct)
    {
        if (id == Guid.Empty)
        {
            throw new ManagementException(400, "Yêu cầu mã bàn hợp lệ.");
        }

        var actor = await RequireCleaningWaiter(null, ct);
        var result = await transaction.ExecuteAsync(async () =>
        {
            var table = await repository.LockForSessionAsync(id, ct)
                ?? throw new ManagementException(404, "Không tìm thấy bàn.");
            if (await RequireCleaningWaiter(table.Area.RestaurantId, ct) != actor)
            {
                throw new ManagementException(403, "Phiên đăng nhập nhân viên đã thay đổi.");
            }

            ManagementValidation.Version(table.Version, version);
            await EnsureIdle(id, ct);
            if (table.CleaningStatus is not ("Dirty" or "Clean"))
            {
                throw new ManagementException(409, "Trạng thái dọn bàn chưa được hỗ trợ. Vui lòng kiểm tra lại.");
            }

            table.CleaningStatus = "Clean";
            return table;
        }, ct);
        return Map(result);
    }

    private async Task<Guid> RequireCleaningWaiter(Guid? restaurantId, CancellationToken ct)
    {
        var actor = await scope.GetCurrentScope(restaurantId, ct);
        if (actor.UserId == Guid.Empty || actor.IsAdministrator
            || !settings.Value.WaiterRoleIds.Contains(actor.RoleId, StringComparer.Ordinal)
            || (restaurantId.HasValue && !actor.RestaurantIds.Contains(restaurantId.Value)))
        {
            throw new ManagementException(403, "Chỉ phục vụ được phân công tại nhà hàng mới được xác nhận dọn xong.");
        }

        return actor.UserId;
    }
}
