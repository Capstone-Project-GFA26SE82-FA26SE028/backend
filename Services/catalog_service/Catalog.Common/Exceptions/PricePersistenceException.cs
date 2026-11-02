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
    Exception innerException) : Exception("Không thể lưu thay đổi của giá.", innerException)
{
    public PricePersistenceFailure Failure { get; } = failure;
}
