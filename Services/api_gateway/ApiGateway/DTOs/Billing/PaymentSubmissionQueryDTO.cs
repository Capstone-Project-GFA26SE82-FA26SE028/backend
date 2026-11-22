namespace ApiGateway.DTOs.Billing;

public sealed class PaymentSubmissionQueryDTO
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Search { get; set; }
    public Guid? AreaId { get; set; }
    public Guid? CashShiftId { get; set; }
}
