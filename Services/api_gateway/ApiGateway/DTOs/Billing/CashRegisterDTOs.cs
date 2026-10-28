namespace ApiGateway.DTOs.Billing;

public sealed class CreateCashRegisterDTO
{
    public Guid RestaurantId { get; set; }
    public string Code { get; set; } = string.Empty;
}

public sealed class CashRegisterQueryDTO
{
    public Guid? RestaurantId { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed record CashRegisterDTO(Guid CashRegisterId, Guid RestaurantId, string Code);
