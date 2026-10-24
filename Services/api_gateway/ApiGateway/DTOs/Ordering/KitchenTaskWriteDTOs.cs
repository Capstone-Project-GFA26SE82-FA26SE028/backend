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

public sealed class KitchenBatchActionDTO
{
    public List<Guid> TaskIds { get; set; } = [];
}

public sealed class KitchenTaskRemakeDTO
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class FireCourseRequestDTO
{
    public Guid TableSessionId { get; set; }
    public Guid? OrderId { get; set; }
    public string Course { get; set; } = "Dessert";
}

