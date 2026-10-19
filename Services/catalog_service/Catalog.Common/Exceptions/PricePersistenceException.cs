namespace Catalog.Common.Exceptions;

public enum PricePersistenceFailure
{
    Conflict,
    RelatedDataChanged,
    Busy,
    Unavailable
}

public sealed class PricePersistenceException(
    PricePersistenceFailure failure,
    Exception innerException) : Exception("The price operation could not be persisted.", innerException)
{
    public PricePersistenceFailure Failure { get; } = failure;
}
