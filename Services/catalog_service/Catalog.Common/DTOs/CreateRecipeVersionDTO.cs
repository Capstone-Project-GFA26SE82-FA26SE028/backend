namespace Catalog.Common.DTOs;

public sealed class CreateRecipeVersionDTO
{
    public Guid? ItemId { get; set; }
    public Guid? OptionId { get; set; }
    public DateTime? EffectiveFrom { get; set; }
    public string? Instructions { get; set; }
    public List<CreateRecipeIngredientDTO> Ingredients { get; set; } = new();
    public List<CreateRecipeMediaDTO> Media { get; set; } = new();
}
