namespace ApiGateway.DTOs.Customer;

public sealed class CreateCustomerRequestDTO
{
    public string? PhoneNumber { get; set; }
    public string? CustomerName { get; set; }
}

public sealed class EarnPointsRequestDTO
{
    public Guid CustomerId { get; set; }
    public Guid BillId { get; set; }
    public decimal FinalAmount { get; set; }
}

public sealed class ReservePointsRequestDTO
{
    public Guid CustomerId { get; set; }
    public Guid BillId { get; set; }
    public int Points { get; set; }
}

public sealed class BillPointsRequestDTO
{
    public Guid BillId { get; set; }
}

public sealed class FeedbackItemRequestDTO
{
    public Guid OrderItemRef { get; set; }
    public int Star { get; set; }
    public string? Comment { get; set; }
}

public sealed class SubmitFeedbackRequestDTO
{
    public Guid OrderRef { get; set; }
    public string? AccessToken { get; set; }
    public Guid? CustomerId { get; set; }
    public int Star { get; set; }
    public string? Comment { get; set; }
    public List<FeedbackItemRequestDTO>? Items { get; set; }
}

public sealed class CreateFeedbackAccessDTO
{
    public Guid OrderRef { get; set; }
}

