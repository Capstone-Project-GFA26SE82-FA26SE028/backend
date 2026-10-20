namespace Catalog.Common.DTOs;

public sealed class RecipeIngredientDTO
{
    public Guid RecipeIngredientId { get; set; }
    public Guid RecipeVersionId { get; set; }
    public Guid IngredientRef { get; set; }
    public Guid UnitRef { get; set; }
    public decimal Quantity { get; set; }
    public decimal BaseQuantitySnapshot { get; set; }
    public decimal ConversionFactorSnapshot { get; set; }
}
