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
    Exception innerException) : Exception("Không thể lưu thay đổi của món ăn.", innerException)
{
    public ItemPersistenceFailure Failure { get; } = failure;
}
