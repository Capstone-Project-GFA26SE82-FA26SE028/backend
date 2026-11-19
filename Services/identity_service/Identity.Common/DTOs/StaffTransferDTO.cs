namespace Identity.Common.DTOs;

public sealed record StaffTransferDTO(Guid Id, Guid UserId, Guid SourceRestaurantId, Guid TargetRestaurantId,
    string Status, string? Message, DateTime CreatedAt, DateTime? CompletedAt);
public sealed record StaffTransferResult(string Status, string? Message);
