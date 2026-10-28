namespace Ordering.Common.DTOs;

public sealed class CheckoutLockDTO
{
    public Guid CheckoutId { get; set; }
    public Guid TableSessionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? SourceHash { get; set; }
}
