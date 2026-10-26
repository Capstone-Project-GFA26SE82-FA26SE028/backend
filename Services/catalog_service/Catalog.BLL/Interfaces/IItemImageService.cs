namespace Catalog.BLL.Interfaces;

public interface IItemImageService
{
    Task<string> Upload(byte[] image, CancellationToken ct);
}
