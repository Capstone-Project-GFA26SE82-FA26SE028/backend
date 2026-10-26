using Ordering.DAL.Models;

namespace Ordering.BLL.Interfaces;

public interface ICookingInventoryClient
{
    Task DeductCookingIngredientsAsync(
        Guid cookingEventId,
        IReadOnlyList<KitchenTask> tasks,
        CancellationToken ct);
}
