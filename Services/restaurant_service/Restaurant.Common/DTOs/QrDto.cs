namespace Restaurant.Common.DTOs;

public sealed class QrDto
{
    public string? ImageUrl { get; set; }
    public Guid QrId { get; set; }
    public Guid TableId { get; set; }
    public string TableCode { get; set; } = "";
    public Guid AreaId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public bool IsUsable { get; set; }
}
