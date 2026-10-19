using Shared.Common.Pagination;

namespace ApiGateway.DTOs.Catalog;

public sealed class OptionQueryDTO
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Search { get; set; }
    public Guid? OptionGroupId { get; set; }
    public string? Availability { get; set; }

    public bool IsValid()
    {
        return new PaginationRequest
        {
            Page = Page,
            PageSize = PageSize
        }.IsValid()
            && (Search is null || Search.Trim().Length <= 255)
            && OptionGroupId != Guid.Empty
            && (Availability is null || Availability.Length <= 50);
    }
}
