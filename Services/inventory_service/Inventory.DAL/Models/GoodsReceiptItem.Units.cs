namespace Inventory.DAL.Models;

public partial class GoodsReceiptItem
{
    public Guid? InputUnitId { get; set; }
    public decimal? InputQuantity { get; set; }
    public decimal? ConversionFactorSnapshot { get; set; }
}
