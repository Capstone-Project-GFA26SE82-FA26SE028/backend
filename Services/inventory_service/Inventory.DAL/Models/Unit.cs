namespace Inventory.DAL.Models;

public sealed class Unit
{
    public Guid UnitId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
}
