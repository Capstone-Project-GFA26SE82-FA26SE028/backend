namespace Catalog.Common.Exceptions;

public sealed class RecipePersistenceException(bool isConflict, Exception innerException)
    : Exception("Không thể lưu phiên bản công thức.", innerException)
{
    public bool IsConflict { get; } = isConflict;
}
