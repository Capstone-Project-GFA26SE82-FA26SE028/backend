using Shared.Common.Pagination;

namespace Catalog.Common.DTOs;

public sealed class PriceQueryDTO
{
    public Guid? ItemId { get; set; }
    public string? Channel { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public bool IsValid()
    {
        return ItemId != Guid.Empty && (Channel is null || Channel.Trim().Length <= 50)
            && new PaginationRequest
            {
                Page = Page,
                PageSize = PageSize
            }.IsValid();
    }
}
