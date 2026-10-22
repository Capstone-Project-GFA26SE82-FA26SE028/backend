namespace Catalog.Common.Exceptions;

public sealed class RecipePersistenceException(bool isConflict, Exception innerException)
    : Exception("Unable to persist the recipe version.", innerException)
{
    public bool IsConflict { get; } = isConflict;
}
