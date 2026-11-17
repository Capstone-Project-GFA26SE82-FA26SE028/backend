namespace ApiGateway.DTOs.Ordering;

public sealed class RefundAccountDTO
{
    public bool Submitted { get; set; }
    public string BankName { get; set; } = "";
    public string AccountNumber { get; set; } = "";
    public string AccountHolder { get; set; } = "";
}
