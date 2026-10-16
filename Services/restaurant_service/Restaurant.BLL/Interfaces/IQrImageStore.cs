namespace Restaurant.BLL.Interfaces;

public interface IQrImageStore
{
    bool Exists(Guid id);

    Task SaveAsync(Guid id, byte[] png, CancellationToken ct);

    Task<byte[]> ReadAsync(Guid id, CancellationToken ct);
}
