using Shared.Common.Pagination;

namespace Catalog.Common.DTOs;

public sealed class ItemQueryDTO
{
    public Guid? CategoryId { get; set; }
    public string? Search { get; set; }
    public string? Availability { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public bool IsValid()
    {
        var pagination = new PaginationRequest
        {
            Page = Page,
            PageSize = PageSize
        };

        return pagination.IsValid()
            && CategoryId != Guid.Empty
            && (Search is null || Search.Trim().Length <= 255)
            && (Availability is null || Availability.Trim().Length <= 50);
    }
}
