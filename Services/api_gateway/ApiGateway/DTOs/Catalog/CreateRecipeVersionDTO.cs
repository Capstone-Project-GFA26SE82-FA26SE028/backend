namespace ApiGateway.DTOs.Catalog;

public sealed class CreateRecipeVersionDTO
{
    public DateTime? EffectiveFrom { get; set; }
    public string? Instructions { get; set; }
    public List<CreateRecipeIngredientDTO> Ingredients { get; set; } = new();
    public List<CreateRecipeMediaDTO> Media { get; set; } = new();
}
