namespace ApiGateway.DTOs.Ordering;

public sealed class KitchenTaskStatusDTO
{
    public string Status { get; set; } = string.Empty;
    public string? Version { get; set; }
}

public sealed class KitchenTaskPriorityDTO
{
    public int Priority { get; set; }
    public string? Version { get; set; }
}

public sealed class ServeOrderItemDTO
{
    public string? Version { get; set; }
}

