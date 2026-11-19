namespace ApiGateway.DTOs.Ordering;

public sealed class KitchenAssignmentWriteDTO
{
    public Guid UserId { get; set; }
    public Guid ExpectedUserId { get; set; }
}
