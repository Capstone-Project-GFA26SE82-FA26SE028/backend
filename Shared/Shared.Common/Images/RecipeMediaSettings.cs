namespace Shared.Common.Images;

public sealed class RecipeMediaSettings
{
    public const int ImageLimitBytes = 2 * 1024 * 1024;
    public const int VideoLimitBytes = 20 * 1024 * 1024;
    public const int TransportLimitBytes = VideoLimitBytes + 65536;

    public int MaximumImageBytes { get; set; } = ImageLimitBytes;
    public int MaximumVideoBytes { get; set; } = VideoLimitBytes;
    public int UploadTimeoutSeconds { get; set; } = 45;

    public bool IsValid()
    {
        return MaximumImageBytes is > 0 and <= ImageLimitBytes
            && MaximumVideoBytes is > 0 and <= VideoLimitBytes
            && UploadTimeoutSeconds is >= 5 and <= 120;
    }
}
