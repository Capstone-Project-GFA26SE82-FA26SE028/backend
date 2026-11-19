namespace ApiGateway.DTOs.Ordering;

public sealed class CreateCancellationRequestDTO
{
    public Guid OrderItemId { get; set; }
    public int Quantity { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class RejectCancellationRequestDTO
{
    public string Reason { get; set; } = "";
}
