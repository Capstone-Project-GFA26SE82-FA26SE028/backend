namespace Catalog.BLL.Interfaces;

public interface IMenuAccess
{
    Task RequireReadAsync(Guid? tableSessionId, string? guestToken, CancellationToken cancellationToken);
}
