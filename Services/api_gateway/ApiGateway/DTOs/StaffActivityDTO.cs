namespace ApiGateway.DTOs;

public sealed class StaffActivityDTO
{
    public Guid ActivityId { get; set; }
    public Guid RestaurantId { get; set; }
    public DateTime Timestamp { get; set; }
    public string ActivityType { get; set; } = string.Empty;
    public Guid? StaffId { get; set; }
    public string? StaffName { get; set; }
    public string? StaffRole { get; set; }
    public string Target { get; set; } = string.Empty;
    public string ActionDescription { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = [];
}

public sealed class StaffActivityQueryDTO
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Search { get; set; }
    public string? ActivityType { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
}
