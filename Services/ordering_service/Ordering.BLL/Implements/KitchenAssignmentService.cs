using Ordering.BLL.Interfaces;
using Ordering.Common.DTOs;
using Ordering.Common.Exceptions;
using Ordering.DAL.Interfaces;
using Ordering.DAL.Models;
using Shared.Common.Pagination;

namespace Ordering.BLL.Implements;

public sealed class KitchenAssignmentService(IKitchenAssignmentRepository repository, IOrderingTransaction transaction,
    IKitchenAssignmentAccess access) : IKitchenAssignmentService
{
    public async Task<PagedResult<KitchenAssignmentDTO>> GetAll(int page, int pageSize, CancellationToken ct)
    {
        await access.RequireManager(ct);
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize)
        {
            throw new OrderingException(400, "Phân trang không hợp lệ; pageSize phải từ 1 đến 100.");
        }

        var result = await repository.GetAllAsync(page, pageSize, ct);
        var employees = await access.GetEmployees(ct);
        return new PagedResult<KitchenAssignmentDTO>(result.Items.Select(item => Map(item, employees)).ToArray(),
            result.Total, page, pageSize);
    }

    public async Task<KitchenAssignmentDTO> GetById(Guid stationId, CancellationToken ct)
    {
        await access.RequireManager(ct);
        var item = await Find(stationId, ct);
        return Map(item, await access.GetEmployees(ct));
    }

    public async Task<IReadOnlyList<KitchenEmployeeDTO>> GetEmployees(CancellationToken ct)
    {
        await access.RequireManager(ct);
        return (await access.GetEmployees(ct)).Where(item => item.IsActive && item.RoleId == "kitchen")
            .OrderBy(item => item.FullName).ThenBy(item => item.UserId).ToArray();
    }

    public Task<KitchenAssignmentDTO> Create(Guid stationId, Guid userId, CancellationToken ct)
    {
        return Save(stationId, userId, null, ct);
    }

    public Task<KitchenAssignmentDTO> Update(Guid stationId, Guid userId, Guid expectedUserId, CancellationToken ct)
    {
        if (expectedUserId == Guid.Empty)
        {
            throw new OrderingException(400, "Cần expectedUserId của phân công hiện tại.");
        }

        return Save(stationId, userId, expectedUserId, ct);
    }

    private async Task<KitchenAssignmentDTO> Save(Guid stationId, Guid userId, Guid? expectedUserId, CancellationToken ct)
    {
        await access.RequireManager(ct);
        ValidateIds(stationId, userId);
        return await transaction.ExecuteAsync(async () =>
        {
            await repository.LockUserAsync(userId, ct);
            var station = await repository.LockStationAsync(stationId, ct)
                ?? throw new OrderingException(404, "Không tìm thấy trạm bếp trong nhà hàng hiện tại.");
            if (!station.IsActive)
            {
                throw new OrderingException(409, "Không thể phân công vào trạm bếp đã ngừng hoạt động.");
            }

            var assignment = await repository.GetByIdAsync(stationId, ct);
            if (expectedUserId.HasValue)
            {
                if (assignment is null || assignment.UserRef != expectedUserId.Value)
                {
                    throw new OrderingException(409, "Phân công đã thay đổi. Vui lòng tải lại trước khi tiếp tục.");
                }
            }
            else if (assignment is not null)
            {
                throw new OrderingException(409, "Trạm bếp đã có người phụ trách; hãy dùng thao tác đổi phân công.");
            }

            if (assignment is not null && assignment.UserRef != userId && await repository.HasCookingTasksAsync(stationId, ct))
            {
                throw new OrderingException(409, "Trạm bếp còn món đang chế biến; cần hoàn tất trước khi đổi người phụ trách.");
            }

            await access.RequireManager(ct);
            await access.RequireEmployee(userId, ct);
            if (assignment is null)
            {
                assignment = new KitchenAssignment
                {
                    KitchenStationId = stationId,
                    KitchenStation = station,
                    UserRef = userId
                };
                repository.Create(assignment);
            }
            else
            {
                assignment.UserRef = userId;
            }

            return Map(assignment, await access.GetEmployees(ct));
        }, ct);
    }

    public async Task Delete(Guid stationId, Guid expectedUserId, CancellationToken ct)
    {
        await access.RequireManager(ct);
        ValidateIds(stationId, expectedUserId);
        await transaction.ExecuteAsync(async () =>
        {
            await repository.LockUserAsync(expectedUserId, ct);
            if (await repository.LockStationAsync(stationId, ct) is null)
            {
                throw new OrderingException(404, "Không tìm thấy trạm bếp trong nhà hàng hiện tại.");
            }

            var assignment = await Find(stationId, ct);
            if (assignment.UserRef != expectedUserId)
            {
                throw new OrderingException(409, "Phân công đã thay đổi. Vui lòng tải lại trước khi gỡ.");
            }

            if (await repository.HasCookingTasksAsync(stationId, ct) || await repository.HasPendingTasksAsync(stationId, ct))
            {
                throw new OrderingException(409, "Trạm còn món chờ hoặc đang chế biến; hãy xử lý hết hoặc đổi người phụ trách.");
            }

            await access.RequireManager(ct);
            repository.Delete(assignment);
            return true;
        }, ct);
    }

    private async Task<KitchenAssignment> Find(Guid stationId, CancellationToken ct)
    {
        return await repository.GetByIdAsync(stationId, ct)
            ?? throw new OrderingException(404, "Trạm bếp chưa được phân công hoặc không thuộc nhà hàng hiện tại.");
    }

    private static void ValidateIds(Guid stationId, Guid userId)
    {
        if (stationId == Guid.Empty || userId == Guid.Empty)
        {
            throw new OrderingException(400, "Cần mã trạm bếp và nhân viên hợp lệ.");
        }
    }

    private static KitchenAssignmentDTO Map(KitchenAssignment item, IReadOnlyList<KitchenEmployeeDTO> employees)
    {
        return new KitchenAssignmentDTO
        {
            KitchenStationId = item.KitchenStationId,
            KitchenStationCode = item.KitchenStation.KitchenStationCode,
            KitchenStationName = item.KitchenStation.KitchenStationName,
            StationActive = item.KitchenStation.IsActive,
            UserId = item.UserRef,
            Employee = employees.FirstOrDefault(employee => employee.UserId == item.UserRef)
        };
    }
}
