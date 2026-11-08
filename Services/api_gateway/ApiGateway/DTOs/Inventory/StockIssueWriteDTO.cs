using System.ComponentModel.DataAnnotations;

namespace ApiGateway.DTOs.Inventory;

public sealed class StockIssueWriteDTO
{
    public Guid StockIssueId { get; set; }
    [Required(ErrorMessage = "Trường {0} là bắt buộc."), StringLength(1000, ErrorMessage = "Trường {0} phải có độ dài từ {2} đến {1} ký tự.")]
    public string Reason { get; set; } = "";
    [Required(ErrorMessage = "Trường {0} là bắt buộc."), MinLength(1, ErrorMessage = "Trường {0} phải có ít nhất {1} phần tử hoặc ký tự."), MaxLength(100, ErrorMessage = "Trường {0} chỉ được có tối đa {1} phần tử hoặc ký tự.")]
    public List<StockIssueLineDTO> Items { get; set; } = [];
}

public sealed class StockIssueLineDTO
{
    public Guid IngredientId { get; set; }
    public decimal Quantity { get; set; }
}
