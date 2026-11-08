namespace Restaurant.BLL.Interfaces;

public interface IQrImageStore
{
    Task<string> SaveAsync(Guid id, byte[] png, CancellationToken ct);

    Task<byte[]> ReadAsync(string url, CancellationToken ct);
}
