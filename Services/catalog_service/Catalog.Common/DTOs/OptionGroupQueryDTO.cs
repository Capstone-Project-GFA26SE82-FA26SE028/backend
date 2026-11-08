using Shared.Common.Pagination;

namespace Catalog.Common.DTOs;

public sealed class OptionGroupQueryDTO
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Search { get; set; }

    public bool IsValid()
    {
        return new PaginationRequest
        {
            Page = Page,
            PageSize = PageSize
        }.IsValid()
            && (Search is null || Search.Trim().Length <= 255);
    }
}
