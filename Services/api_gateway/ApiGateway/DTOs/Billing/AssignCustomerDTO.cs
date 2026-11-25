namespace ApiGateway.DTOs.Billing;

public sealed class AssignCustomerDTO
{
    public Guid? CustomerId { get; set; }
    public string? PhoneNumber { get; set; }
}
