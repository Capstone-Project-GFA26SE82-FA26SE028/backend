namespace Catalog.Common.DTOs;

public sealed class CreateRecipeMediaDTO
{
    public string? MediaType { get; set; }
    public string? Url { get; set; }
    public int DisplayOrder { get; set; }
}
