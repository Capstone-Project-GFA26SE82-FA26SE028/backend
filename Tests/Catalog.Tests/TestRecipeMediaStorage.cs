using Catalog.BLL.Interfaces;
using Shared.Common.Images;

namespace Catalog.Tests;

internal sealed class TestRecipeMediaStorage : IRecipeMediaStorage
{
    public int Calls { get; private set; }
    public int? FailureStatus { get; set; }

    public Task<string> UploadAsync(byte[] file, string mediaType, string publicId, CancellationToken ct)
    {
        Calls++;
        if (FailureStatus.HasValue)
        {
            throw new ImageStorageException(FailureStatus.Value, "Cloud upload failed.");
        }

        return Task.FromResult($"https://res.cloudinary.com/test/{mediaType}/upload/v1700000000/{publicId}."
            + (mediaType == "image" ? "png" : "mp4"));
    }
}
