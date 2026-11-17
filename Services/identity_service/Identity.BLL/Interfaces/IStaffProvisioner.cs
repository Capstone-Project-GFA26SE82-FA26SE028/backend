namespace Identity.BLL.Interfaces;

public interface IStaffProvisioner
{
    bool IsConfigured { get; }
    Task<bool> EnsureAssignmentAsync(Guid restaurantId, Guid userId, CancellationToken ct);
}
