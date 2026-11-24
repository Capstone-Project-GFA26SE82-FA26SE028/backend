using Restaurant.Common.Exceptions;
using Shared.Common.Images;

namespace Restaurant.BLL.Implements;

public sealed partial class RestaurantService
{
    public async Task<string> UploadLogo(Guid id, byte[] image, CancellationToken ct)
    {
        await access.RequireAdmin(ct);
        if (id == Guid.Empty)
        {
            throw new ManagementException(400, "ID nhà hàng không hợp lệ.");
        }

        if (await repository.GetByIdAsync(id, ct) is null)
        {
            throw new ManagementException(404, "Không tìm thấy nhà hàng.");
        }

        if (image.Length == 0 || image.Length > CloudinaryImageStorage.MaxImageBytes || !IsLogoImage(image))
        {
            throw new ManagementException(400, "Chọn logo JPEG, PNG hoặc WebP hợp lệ, tối đa 2 MiB.");
        }

        if (images is null)
        {
            throw new ManagementException(503, "Dịch vụ tải logo chưa được cấu hình.");
        }

        string url;
        try
        {
            url = await images.UploadAsync(image, $"restaurant/logos/{id:N}/{Guid.NewGuid():N}", ct);
        }
        catch (ImageStorageException error)
        {
            throw new ManagementException(error.StatusCode, error.Message);
        }

        await access.RevalidateAdmin(ct);
        return await transaction.ExecuteAsync(async () =>
        {
            var item = await repository.LockAsync(id, ct)
                ?? throw new ManagementException(404, "Không tìm thấy nhà hàng.");
            item.LogoUrl = url;
            return url;
        }, ct);
    }

    private static bool IsLogoImage(byte[] image)
    {
        ReadOnlySpan<byte> bytes = image;
        return bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || bytes.StartsWith(new byte[] { 255, 216, 255 })
            || (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8));
    }

    private static ManagementException LogoUploadRequired()
    {
        return new ManagementException(400, "Vui lòng tải file qua API logo; không nhập đường dẫn logo mới.");
    }
}
