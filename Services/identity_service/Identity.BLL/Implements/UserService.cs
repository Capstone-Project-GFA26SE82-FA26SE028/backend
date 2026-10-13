using Shared.Common.Pagination;
using Shared.Common.DTOs;
using Identity.BLL.Interfaces;
using Identity.Common.DTOs;
using Identity.Common.Settings;
using Identity.DAL.UnitOfWorks;
using Identity.Domain.Models;
using Microsoft.Extensions.Options;

namespace Identity.BLL.Implements;

public sealed class UserService(IUnitOfWork unitOfWork, IOptions<UserManagementSettings> settings) : IUserService
{
    private static ResponseDTO Missing()
    {
        return new("Không tìm thấy user.", 404);
    }

    private static ResponseDTO Forbidden()
    {
        return new("Bạn không có quyền quản lý user.", 403);
    }

    private async Task<bool> IsAdmin(Guid actorId, CancellationToken ct)
    {
        var actor = await unitOfWork.UserRepo.FindUserAsync(actorId, ct);
        return actor is { IsActive: true } && settings.Value.AdminRoleIds.Contains(actor.RoleId, StringComparer.Ordinal);
    }

    public async Task<ResponseDTO> GetMe(Guid actorId, CancellationToken ct)
    {
        var user = await unitOfWork.UserRepo.FindUserAsync(actorId, ct);
        return user is null ? Missing() : new("Thành công.", 200, true, Map(user));
    }

    public async Task<ResponseDTO> UpdateMe(Guid actorId, UpdateProfileDTO request, CancellationToken ct)
    {
        var name = request.FullName?.Trim();
        var phone = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
        var avatar = string.IsNullOrWhiteSpace(request.AvatarUrl) ? null : request.AvatarUrl.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 255 || phone?.Length > 20
            || avatar?.Length > 2048 || (avatar is not null &&
                (!Uri.TryCreate(avatar, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))))
        {
            return new("Tên bắt buộc (tối đa 255 ký tự), số điện thoại tối đa 20 ký tự, avatar phải là URL HTTP/HTTPS hợp lệ.", 400);
        }

        var user = await unitOfWork.UserRepo.FindUserAsync(actorId, ct);
        if (user is null)
        {
            return Missing();
        }

        unitOfWork.RecordAudit(actorId, "UPDATE_PROFILE", "User", user.UserId.ToString(),
            new
            {
                user.FullName,
                user.PhoneNumber,
                user.AvatarUrl
            },
            new
            {
                FullName = name,
                PhoneNumber = phone,
                AvatarUrl = avatar
            });
        user.FullName = name;
        user.PhoneNumber = phone!;
        user.AvatarUrl = avatar!;
        user.UpdatedAt = DateTime.UtcNow;
        await unitOfWork.SaveAsync(ct);
        return new("Cập nhật hồ sơ thành công.", 200, true, Map(user));
    }

    public async Task<ResponseDTO> GetAll(Guid actorId, int page, int pageSize, string? search, CancellationToken ct)
    {
        if (!await IsAdmin(actorId, ct))
        {
            return Forbidden();
        }

        var pagination = new PaginationRequest
        {
            Page = page,
            PageSize = pageSize
        };

        if (!pagination.IsValid() || search?.Length > 255)
        {
            return new("Phân trang hoặc từ khóa không hợp lệ. PageSize từ 1 đến 100.", 400);
        }

        var (items, count) = await unitOfWork.UserRepo.GetAllAsync(page, pageSize, search?.Trim(), ct);
        return new("Thành công.", 200, true, new PagedResult<UserDTO>(items.Select(Map).ToList(), count, page, pageSize));
    }

    public async Task<ResponseDTO> GetById(Guid actorId, Guid userId, CancellationToken ct)
    {
        if (!await IsAdmin(actorId, ct))
        {
            return Forbidden();
        }

        return await GetMe(userId, ct);
    }

    public async Task<ResponseDTO> SetStatus(Guid actorId, Guid userId, bool isActive, CancellationToken ct)
    {
        if (!await IsAdmin(actorId, ct))
        {
            return Forbidden();
        }

        if (actorId == userId && !isActive)
        {
            return new("Không thể tự khóa tài khoản của mình.", 400);
        }

        return AccessResponse(await unitOfWork.ExecuteInTransactionAsync(() => unitOfWork.UserRepo.ChangeAccessAsync(actorId, userId, null, isActive, settings.Value.AdminRoleIds, ct), ct));
    }

    public async Task<ResponseDTO> Create(Guid actorId, CreateUserDTO request, CancellationToken ct)
    {
        if (!await IsAdmin(actorId, ct))
        {
            return Forbidden();
        }

        var name = request.UserName?.Trim();
        var email = request.Email?.Trim();
        var fullName = request.FullName?.Trim();
        var role = request.RoleId?.Trim();
        var phone = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 100 || name.Any(char.IsWhiteSpace)
            || string.IsNullOrEmpty(email) || email.Length > 255 || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email)
            || string.IsNullOrEmpty(fullName) || fullName.Length > 255 || string.IsNullOrEmpty(role) || role.Length > 50
            || phone?.Length > 20 || string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8
            || System.Text.Encoding.UTF8.GetByteCount(request.Password) > 72)
        {
            return new("Dữ liệu không hợp lệ. Mật khẩu cần ít nhất 8 ký tự và tối đa 72 byte UTF-8.", 400);
        }

        var user = new User
        {
            UserId = Guid.NewGuid(),
            UserName = name,
            Email = email,
            FullName = fullName,
            PhoneNumber = phone!,
            RoleId = role,
            IsActive = true,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            CreatedAt = DateTime.UtcNow
        };
        var result = await unitOfWork.ExecuteInTransactionAsync(() => unitOfWork.UserRepo.CreateEmployeeAsync(actorId, user, settings.Value.AdminRoleIds, ct), ct);
        return result.Status switch
        {
            201 => new("Tạo tài khoản thành công.", 201, true, Map(result.User!)),
            409 => new("Email hoặc username đã tồn tại.", 409),
            400 => new("Role không tồn tại.", 400),
            _ => Forbidden()
        };
    }

    public async Task<ResponseDTO> SetRole(Guid actorId, Guid userId, string roleId, CancellationToken ct)
    {
        if (!await IsAdmin(actorId, ct))
        {
            return Forbidden();
        }

        if (string.IsNullOrWhiteSpace(roleId) || roleId.Trim().Length > 50)
        {
            return new("Role không hợp lệ.", 400);
        }

        return AccessResponse(await unitOfWork.ExecuteInTransactionAsync(() => unitOfWork.UserRepo.ChangeAccessAsync(actorId, userId, roleId.Trim(), null, settings.Value.AdminRoleIds, ct), ct));
    }

    private static ResponseDTO AccessResponse((int Status, User? User) result)
    {
        return result.Status switch
        {
            200 => new("Cập nhật thành công.", 200, true, Map(result.User!)),
            404 => Missing(),
            403 => Forbidden(),
            409 => new("Không thể loại bỏ Admin hoạt động cuối cùng.", 409),
            _ => new("Role không tồn tại.", 400)
        };
    }

    private static UserDTO Map(User user)
    {
        return new()
        {
            UserId = user.UserId,
            RoleId = user.RoleId,
            UserName = user.UserName,
            Email = user.Email,
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber,
            AvatarUrl = user.AvatarUrl,
            IsActive = user.IsActive,
            CreatedAt = DateTime.SpecifyKind(user.CreatedAt, DateTimeKind.Utc),
            UpdatedAt = user.UpdatedAt is { } updated ? DateTime.SpecifyKind(updated, DateTimeKind.Utc) : null
        };
    }
}
