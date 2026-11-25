using Shared.Common.DTOs;
using Shared.Common.Images;

namespace Identity.BLL.Implements;

public sealed partial class UserService
{
    public async Task<ResponseDTO> UploadAvatar(Guid actorId, Guid userId, byte[] image, CancellationToken ct)
    {
        if (!await CanUploadAvatar(actorId, userId, ct))
        {
            return Forbidden();
        }

        if (await unitOfWork.UserRepo.FindUserAsync(userId, ct) is null)
        {
            return Missing();
        }

        if (image.Length == 0 || image.Length > CloudinaryImageStorage.MaxImageBytes || !IsAvatarImage(image))
        {
            return new ResponseDTO("Chọn ảnh JPEG, PNG hoặc WebP hợp lệ, tối đa 2 MiB.", 400);
        }

        if (images is null)
        {
            return new ResponseDTO("Dịch vụ tải ảnh chưa được cấu hình.", 503);
        }

        string url;
        try
        {
            url = await images.UploadAsync(image, $"identity/avatars/{userId:N}/{Guid.NewGuid():N}", ct);
        }
        catch (ImageStorageException error)
        {
            return new ResponseDTO(error.Message, error.StatusCode);
        }

        // Recheck access after the external call; a role or assignment may have changed.
        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            foreach (var id in new[] { actorId, userId }.Distinct().Order())
            {
                await unitOfWork.UserRepo.LockAsync(id, ct);
            }

            if (!await CanUploadAvatar(actorId, userId, ct))
            {
                return Forbidden();
            }

            var user = await unitOfWork.UserRepo.FindUserAsync(userId, ct);
            if (user is null)
            {
                return Missing();
            }

            var before = new { user.AvatarUrl };
            user.AvatarUrl = url;
            user.UpdatedAt = DateTime.UtcNow;
            unitOfWork.RecordAudit(actorId, "UPDATE_AVATAR", "User", userId.ToString(), before, new { user.AvatarUrl });
            return new ResponseDTO("Cập nhật ảnh đại diện thành công.", 200, true, Map(user));
        }, ct);
    }

    private async Task<bool> CanUploadAvatar(Guid actorId, Guid userId, CancellationToken ct)
    {
        var actor = await unitOfWork.UserRepo.ReloadUserAsync(actorId, ct);
        if (actor is null || !actor.IsActive || userId == Guid.Empty)
        {
            return false;
        }

        if (actorId == userId)
        {
            return true;
        }

        await unitOfWork.UserRepo.ReloadUserAsync(userId, ct);
        return await CanManage(actorId, userId, ct);
    }

    private static bool IsAvatarImage(byte[] image)
    {
        ReadOnlySpan<byte> bytes = image;
        return bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || bytes.StartsWith(new byte[] { 255, 216, 255 })
            || (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8));
    }

    private static bool CanKeepAvatar(string? requested, string? existing)
    {
        return requested is null || string.Equals(requested, existing, StringComparison.Ordinal);
    }

    private static ResponseDTO AvatarUploadRequired()
    {
        return new ResponseDTO("Vui lòng tải file ảnh qua API avatar; không nhập đường dẫn ảnh mới.", 400);
    }
}
