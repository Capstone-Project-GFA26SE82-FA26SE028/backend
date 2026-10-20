using System.Text.Json.Serialization;

namespace Catalog.Common.DTOs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ValidateOrderItemsDTO
{
    public string Channel { get; set; } = "DineIn";
    public List<OrderSelectionDTO> Items { get; set; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class OrderSelectionDTO
{
    public Guid ItemId { get; set; }
    public int Quantity { get; set; }
    public List<OrderOptionSelectionDTO> Options { get; set; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class OrderOptionSelectionDTO
{
    public Guid OptionId { get; set; }
    public int Quantity { get; set; }
}
