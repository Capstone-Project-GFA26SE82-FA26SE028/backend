namespace Catalog.Common.Exceptions;

public enum ItemPersistenceFailure
{
    Duplicate,
    RelatedDataChanged,
    ConcurrentUpdate,
    Busy,
    Unavailable
}

public sealed class ItemPersistenceException(
    ItemPersistenceFailure failure,
    Exception innerException) : Exception("The item operation could not be persisted.", innerException)
{
    public ItemPersistenceFailure Failure { get; } = failure;
}
