namespace Restaurant.BLL.Interfaces;

public interface IRestaurantRegistrationDocuments
{
    Task<string> UploadAsync(byte[] image, string publicId, CancellationToken ct);
    Task<byte[]> ReadAsync(string assetId, CancellationToken ct);
}
