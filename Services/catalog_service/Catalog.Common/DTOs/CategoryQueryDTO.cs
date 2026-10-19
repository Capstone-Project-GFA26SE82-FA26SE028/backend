using Shared.Common.Pagination;

namespace Catalog.Common.DTOs;

public sealed class CategoryQueryDTO
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Search { get; set; }
    public bool? IsActive { get; set; }

    public bool IsValid()
    {
        var pagination = new PaginationRequest
        {
            Page = Page,
            PageSize = PageSize
        };

        return pagination.IsValid()
            && (Search is null || Search.Trim().Length <= 255);
    }
}
