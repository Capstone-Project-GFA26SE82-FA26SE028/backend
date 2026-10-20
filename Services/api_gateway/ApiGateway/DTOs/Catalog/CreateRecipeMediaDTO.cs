namespace ApiGateway.DTOs.Catalog;

public sealed class CreateRecipeMediaDTO
{
    public string? MediaType { get; set; }
    public string? Url { get; set; }
    public int DisplayOrder { get; set; }
}
