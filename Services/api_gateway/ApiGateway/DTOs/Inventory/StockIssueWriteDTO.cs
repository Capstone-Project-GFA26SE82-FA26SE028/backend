using System.ComponentModel.DataAnnotations;

namespace ApiGateway.DTOs.Inventory;

public sealed class StockIssueWriteDTO
{
    public Guid StockIssueId { get; set; }
    [Required, StringLength(1000)]
    public string Reason { get; set; } = "";
    [Required, MinLength(1), MaxLength(100)]
    public List<StockIssueLineDTO> Items { get; set; } = [];
}

public sealed class StockIssueLineDTO
{
    public Guid IngredientId { get; set; }
    public decimal Quantity { get; set; }
}
