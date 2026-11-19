using Identity.Common.DTOs;

namespace Identity.BLL.Interfaces;

public interface IStaffTransferRemote
{
    bool IsConfigured { get; }
    Task<StaffTransferResult> ApplyAsync(Guid id, Guid userId, Guid source, Guid target, CancellationToken ct);
}
