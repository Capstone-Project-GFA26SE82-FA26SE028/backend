namespace Ordering.Common.DTOs;

public sealed class KitchenTaskOptionDTO
{
    public Guid OptionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

public sealed class KitchenTaskDTO
{
    public Guid KitchenTaskId { get; set; }
    public Guid KitchenStationId { get; set; }
    public string KitchenStationCode { get; set; } = string.Empty;
    public string KitchenStationName { get; set; } = string.Empty;
    public Guid OrderItemId { get; set; }
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid TableSessionId { get; set; }
    public Guid? OriginalTableId { get; set; }
    public Guid ItemId { get; set; }
    public string NameSnapshot { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string? Note { get; set; }
    public string Status { get; set; } = string.Empty;
    public int Priority { get; set; }
    public DateTime QueuedAt { get; set; }
    public DateTime? CookingAt { get; set; }
    public DateTime? ReadyAt { get; set; }
    public int? EstimatedMinutes { get; set; }
    public DateTime? DueAt { get; set; }
    public bool IsLate { get; set; }
    public string Version { get; set; } = string.Empty;
    public List<KitchenTaskOptionDTO> Options { get; set; } = [];
}

public sealed class KitchenTaskPageDTO
{
    public List<KitchenTaskDTO> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public sealed class KitchenSummaryDTO
{
    public int PendingCount { get; set; }
    public int CookingCount { get; set; }
    public int ReadyCount { get; set; }
    public double AvgCookingTimeMinutes { get; set; }
}

public sealed class KitchenTaskStatusUpdateDTO
{
    public string Status { get; set; } = string.Empty;
    public string? Version { get; set; }
}

public sealed class KitchenTaskPriorityUpdateDTO
{
    public int Priority { get; set; }
    public string? Version { get; set; }
}

public sealed class KitchenBatchItemDTO
{
    public Guid KitchenTaskId { get; set; }
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string TableCode { get; set; } = string.Empty;
    public int? EstimatedMinutes { get; set; }
    public int Quantity { get; set; }
    public string? Note { get; set; }
    public int Priority { get; set; }
    public DateTime QueuedAt { get; set; }
}

public sealed class KitchenBatchGroupDTO
{
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public Guid KitchenStationId { get; set; }
    public string KitchenStationName { get; set; } = string.Empty;
    public int TotalQuantity { get; set; }
    public List<KitchenBatchItemDTO> Tasks { get; set; } = [];
}

public sealed class KitchenTaskRemakeDTO
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class FireCourseRequestDTO
{
    public Guid? OrderId { get; set; }
    public string Course { get; set; } = string.Empty;
}

public sealed class FireCourseResultDTO
{
    public int ActivatedCount { get; set; }
    public string Message { get; set; } = string.Empty;
}


