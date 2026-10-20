namespace ApiGateway.DTOs.Catalog;

public sealed class RecipeMediaDTO
{
    public Guid RecipeMediaId { get; set; }
    public Guid RecipeVersionId { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}
