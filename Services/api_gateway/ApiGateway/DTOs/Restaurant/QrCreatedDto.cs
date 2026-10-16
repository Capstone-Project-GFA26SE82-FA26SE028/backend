namespace ApiGateway.DTOs.Restaurant;

public sealed class QrCreatedDto
{
    public QrDto Qr { get; set; } = new();
    public string MenuUrl { get; set; } = "";
    public string Token { get; set; } = "";
    public byte[] Png { get; set; } = [];
}
