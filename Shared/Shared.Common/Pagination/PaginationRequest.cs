namespace Shared.Common.Pagination;

public sealed class PaginationRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public bool IsValid()
    {
        return Page >= 1 && PageSize >= 1 && PageSize <= 100
            && (long)(Page - 1) * PageSize <= int.MaxValue;
    }
}
