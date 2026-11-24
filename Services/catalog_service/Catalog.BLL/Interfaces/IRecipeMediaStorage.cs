namespace Catalog.BLL.Interfaces;

public interface IRecipeMediaStorage
{
    Task<string> UploadAsync(byte[] file, string mediaType, string publicId, CancellationToken ct);
}
