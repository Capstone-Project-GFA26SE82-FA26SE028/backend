using Restaurant.BLL.Interfaces;
using Restaurant.Common.Exceptions;

namespace Restaurant.BLL.Implements;

public sealed class QrImageStore(string directory) : IQrImageStore
{
    private readonly string _directory = Path.GetFullPath(directory);

    private string ImagePath(Guid id)
    {
        return Path.Combine(_directory, id.ToString("N") + ".png");
    }

    public bool Exists(Guid id)
    {
        return File.Exists(ImagePath(id));
    }

    public async Task SaveAsync(Guid id, byte[] png, CancellationToken ct)
    {
        var temporary = Path.Combine(_directory, id.ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(_directory);
            await File.WriteAllBytesAsync(temporary, png, ct);
            File.Move(temporary, ImagePath(id));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ManagementException(503, "Không lưu được ảnh QR. Kiểm tra kho lưu ảnh.");
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A failed temporary-file cleanup must not hide the original error.
            }
        }
    }

    public async Task<byte[]> ReadAsync(Guid id, CancellationToken ct)
    {
        try
        {
            return await File.ReadAllBytesAsync(ImagePath(id), ct);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new ManagementException(404, "Chưa có ảnh QR lưu trữ. Hãy tạo lại QR nếu cần ảnh.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ManagementException(503, "Không đọc được kho ảnh QR.");
        }
    }
}
