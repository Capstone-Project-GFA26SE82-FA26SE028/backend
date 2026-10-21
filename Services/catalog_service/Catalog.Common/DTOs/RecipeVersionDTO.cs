namespace Catalog.Common.DTOs;

public sealed class RecipeVersionDTO
{
    public Guid RecipeVersionId { get; set; }
    public Guid? ItemId { get; set; }
    public Guid? OptionId { get; set; }
    public int Version { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public string? Instructions { get; set; }
    public List<RecipeIngredientDTO> Ingredients { get; set; } = new();
    public List<RecipeMediaDTO> Media { get; set; } = new();
}
