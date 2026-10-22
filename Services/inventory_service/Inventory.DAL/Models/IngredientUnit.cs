namespace Inventory.DAL.Models;

public sealed class IngredientUnit
{
    public Guid IngredientId { get; set; }
    public Guid UnitId { get; set; }
    public decimal ConversionFactor { get; set; }
}
