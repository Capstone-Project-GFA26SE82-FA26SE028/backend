namespace Catalog.Common.DTOs;

public sealed class CreateRecipeIngredientDTO
{
    public Guid IngredientRef { get; set; }
    public Guid UnitRef { get; set; }
    public decimal Quantity { get; set; }
    public decimal? ConversionFactor { get; set; }
}
