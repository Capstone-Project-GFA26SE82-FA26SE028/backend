using Microsoft.EntityFrameworkCore;
using Ordering.DAL.Interfaces;
using Ordering.DAL.Models;

namespace Ordering.DAL.Implements;

public sealed class KitchenAssignmentRepository(OrderingDbContext db) : IKitchenAssignmentRepository
{
    public async Task<bool> ExistsForUserAsync(Guid userId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockUserAsync(userId, ct);
        var exists = await db.KitchenAssignments.IgnoreQueryFilters().AnyAsync(item => item.UserRef == userId, ct);
        await transaction.CommitAsync(ct);
        return exists;
    }

    public async Task LockUserAsync(Guid userId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Cần giao dịch để khóa phân công bếp.");
        }

        if (db.Database.IsSqlServer())
        {
            var resource = "KitchenAssignment.User:" + userId.ToString("N");
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @result < 0 THROW 51000, 'Kitchen assignment busy', 1;", ct);
        }
    }

    public Task<KitchenStation?> LockStationAsync(Guid stationId, CancellationToken ct)
    {
        var query = db.Database.IsSqlServer()
            ? db.KitchenStations.FromSqlInterpolated($"SELECT * FROM dbo.KitchenStation WITH (UPDLOCK, HOLDLOCK) WHERE KitchenStationId = {stationId}")
            : db.KitchenStations.Where(item => item.KitchenStationId == stationId);
        return query.SingleOrDefaultAsync(ct);
    }

    public Task<KitchenAssignment?> GetByIdAsync(Guid stationId, CancellationToken ct)
    {
        return db.KitchenAssignments.Include(item => item.KitchenStation)
            .SingleOrDefaultAsync(item => item.KitchenStationId == stationId, ct);
    }

    public async Task<(List<KitchenAssignment> Items, int Total)> GetAllAsync(int page, int pageSize, CancellationToken ct)
    {
        var query = db.KitchenAssignments.AsNoTracking();
        var total = await query.CountAsync(ct);
        var items = await query.Include(item => item.KitchenStation).OrderBy(item => item.KitchenStation.KitchenStationCode)
            .ThenBy(item => item.KitchenStationId).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }

    public Task<bool> HasCookingTasksAsync(Guid stationId, CancellationToken ct)
    {
        return db.KitchenTasks.AnyAsync(item => item.KitchenStationId == stationId && item.Status == "Cooking", ct);
    }

    public Task<bool> HasPendingTasksAsync(Guid stationId, CancellationToken ct)
    {
        return db.KitchenTasks.AnyAsync(item => item.KitchenStationId == stationId && item.Status == "Pending", ct);
    }

    public void Create(KitchenAssignment assignment)
    {
        db.KitchenAssignments.Add(assignment);
    }

    public void Delete(KitchenAssignment assignment)
    {
        db.KitchenAssignments.Remove(assignment);
    }
}
