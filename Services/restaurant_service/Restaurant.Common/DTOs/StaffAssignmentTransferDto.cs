namespace Restaurant.Common.DTOs;

public sealed record StaffAssignmentTransferDto(Guid Id, Guid UserId, Guid SourceRestaurantId, Guid TargetRestaurantId);
public sealed record StaffAssignmentTransferResult(string Status, string? Message);
