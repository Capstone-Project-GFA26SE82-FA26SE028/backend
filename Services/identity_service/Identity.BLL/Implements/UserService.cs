using Shared.Common.Pagination;
using Shared.Common.DTOs;
using Identity.BLL.Interfaces;
using Identity.Common.DTOs;
using Identity.Common.Settings;
using Identity.DAL.UnitOfWorks;
using Identity.Domain.Models;
using Microsoft.Extensions.Options;

namespace Identity.BLL.Implements;

public sealed class UserService(IUnitOfWork unitOfWork, IOptions<UserManagementSettings> settings,
    IUserRestaurantAccess? restaurants = null, IStaffProvisioner? provisioner = null, IRestaurantNames? restaurantNames = null) : IUserService
{
    private static readonly string[] StaffRoles = ["manager", "waiter", "cashier", "kitchen", "inventory"];

    private async Task<bool> CanManage(Guid actorId, Guid? targetId, CancellationToken ct)
    {
        if (await IsAdmin(actorId, ct))
        {
            return true;
        }

        var actor = await unitOfWork.UserRepo.FindUserAsync(actorId, ct);
        if (actor is null || !actor.IsActive || actor.RoleId != "admin" || !actor.RestaurantId.HasValue
            || restaurants is null || !await restaurants.CanManageAsync(actorId, actor.RestaurantId.Value, ct))
        {
            return false;
        }

        if (!targetId.HasValue)
        {
            return true;
        }

        var target = await unitOfWork.UserRepo.FindUserAsync(targetId.Value, ct);
        return target is not null && target.RestaurantId == actor.RestaurantId && StaffRoles.Contains(target.RoleId);
    }

    private async Task<Guid?> ManagedRestaurant(Guid actorId, CancellationToken ct)
    {
        return await IsAdmin(actorId, ct) ? null : (await unitOfWork.UserRepo.FindUserAsync(actorId, ct))?.RestaurantId;
    }

    public async Task<IReadOnlyList<UserDTO>> GetStaffProfiles(Guid restaurantId, Guid[] ids, CancellationToken ct)
    {
        return (await unitOfWork.UserRepo.GetStaffProfilesAsync(restaurantId, ids, ct)).Select(Map).ToArray();
    }

    public async Task ProvisionPending(CancellationToken ct)
    {
        if (provisioner?.IsConfigured != true)
        {
            return;
        }

        foreach (var user in await unitOfWork.UserRepo.GetPendingProvisioningAsync(ct))
        {
            await TryProvision(user, ct);
        }
    }

    private async Task<bool> TryProvision(User user, CancellationToken ct)
    {
        if (!user.RestaurantId.HasValue || provisioner?.IsConfigured != true
            || !await provisioner.EnsureAssignmentAsync(user.RestaurantId.Value, user.UserId, ct))
        {
            return false;
        }

        await unitOfWork.ExecuteInTransactionAsync(() => unitOfWork.UserRepo.CompleteProvisioningAsync(user.UserId, ct), ct);
        user.ProvisioningPending = false;
        user.IsActive = true;
        return true;
    }

    private static ResponseDTO Missing()
    {
        return new("Không tìm thấy người dùng.", 404);
    }

    private static ResponseDTO Forbidden()
    {
        return new("Bạn không có quyền quản lý người dùng.", 403);
    }

    private async Task<bool> IsAdmin(Guid actorId, CancellationToken ct)
    {
        var adminRoles = settings.Value.AdminRoleIds;
        return await unitOfWork.UserRepo.AnyAsync(actor => actor.UserId == actorId
            && actor.IsActive && adminRoles.Contains(actor.RoleId));
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
            return new("Họ tên là bắt buộc (tối đa 255 ký tự), số điện thoại tối đa 20 ký tự và ảnh đại diện phải là đường dẫn URL hợp lệ.", 400);
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
        return new("Cập nhật thông tin cá nhân thành công.", 200, true, Map(user));
    }

    public async Task<ResponseDTO> GetAll(Guid actorId, int page, int pageSize, string? search, CancellationToken ct, Identity.Common.DTOs.UserFilter? filter = null)
    {
        if (!await CanManage(actorId, null, ct))
        {
            return Forbidden();
        }

        var pagination = new PaginationRequest
        {
            Page = page,
            PageSize = pageSize
        };

        if (!pagination.IsValid() || search?.Length > 255 || filter?.RoleId?.Length > 50)
        {
            return new("Phân trang hoặc từ khóa tìm kiếm không hợp lệ. Số lượng mỗi trang phải từ 1 đến 100.", 400);
        }

        var (items, count) = await unitOfWork.UserRepo.GetAllAsync(page, pageSize, search?.Trim(), ct, filter, await ManagedRestaurant(actorId, ct), StaffRoles);
        var results = items.Select(Map).ToList();
        await FillRestaurantNames(results, ct);
        return new("Thành công.", 200, true, new PagedResult<UserDTO>(results, count, page, pageSize));
    }

    public async Task<ResponseDTO> GetById(Guid actorId, Guid userId, CancellationToken ct)
    {
        if (!await CanManage(actorId, userId, ct))
        {
            return Forbidden();
        }

        var response = await GetMe(userId, ct);
        if (response.Result is UserDTO user)
        {
            await FillRestaurantNames([user], ct);
        }

        return response;
    }

    public async Task<ResponseDTO> SetStatus(Guid actorId, Guid userId, bool isActive, CancellationToken ct)
    {
        if (!await CanManage(actorId, userId, ct))
        {
            return Forbidden();
        }

        if (actorId == userId && !isActive)
        {
            return new("Bạn không thể tự vô hiệu hóa tài khoản của chính mình.", 400);
        }

        var managedRestaurant = await ManagedRestaurant(actorId, ct);
        return AccessResponse(await unitOfWork.ExecuteInTransactionAsync(() => unitOfWork.UserRepo.ChangeAccessAsync(actorId, userId, null, isActive, settings.Value.AdminRoleIds, ct, managedRestaurant, StaffRoles), ct));
    }

    public async Task<ResponseDTO> Create(Guid actorId, CreateUserDTO request, CancellationToken ct)
    {
        if (!await CanManage(actorId, null, ct))
        {
            return Forbidden();
        }

        var name = request.UserName?.Trim();
        var email = request.Email?.Trim();
        var fullName = request.FullName?.Trim();
        var role = request.RoleId?.Trim();
        var avatar = string.IsNullOrWhiteSpace(request.AvatarUrl) ? null : request.AvatarUrl.Trim();
        var phone = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 100 || name.Any(char.IsWhiteSpace)
            || string.IsNullOrEmpty(email) || email.Length > 255 || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email)
            || string.IsNullOrEmpty(fullName) || fullName.Length > 255 || string.IsNullOrEmpty(role) || role.Length > 50
            || phone?.Length > 20 || string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8
            || System.Text.Encoding.UTF8.GetByteCount(request.Password) > 72
            || avatar?.Length > 2048 || (avatar is not null
                && (!Uri.TryCreate(avatar, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))))
        {
            return new("Dữ liệu không hợp lệ. Mật khẩu phải chứa ít nhất 8 ký tự và tối đa 72 byte UTF-8.", 400);
        }

        var managedRestaurant = await ManagedRestaurant(actorId, ct);
        if (managedRestaurant.HasValue && (!StaffRoles.Contains(role)
            || (request.RestaurantId.HasValue && request.RestaurantId != managedRestaurant)))
        {
            return Forbidden();
        }

        var owner = managedRestaurant ?? request.RestaurantId;
        if (owner.HasValue && role != "admin" && !StaffRoles.Contains(role))
        {
            return Forbidden();
        }
        if (owner.HasValue && (owner == Guid.Empty || restaurants is null
            || !await restaurants.CanManageAsync(actorId, owner.Value, ct)))
        {
            return Forbidden();
        }

        if (owner.HasValue && provisioner?.IsConfigured != true)
        {
            return new("Chưa cấu hình dịch vụ phân công nhân viên.", 503);
        }

        var user = new User
        {
            UserId = Guid.NewGuid(),
            RestaurantId = owner,
            ProvisioningPending = owner.HasValue,
            UserName = name,
            Email = email,
            FullName = fullName,
            PhoneNumber = phone!,
            RoleId = role,
            IsActive = !owner.HasValue,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            AvatarUrl = avatar!,
            CreatedAt = DateTime.UtcNow
        };
        var result = await unitOfWork.ExecuteInTransactionAsync(() => unitOfWork.UserRepo.CreateEmployeeAsync(actorId, user, settings.Value.AdminRoleIds, ct, managedRestaurant, StaffRoles), ct);
        if (result.Status == 201 && user.ProvisioningPending && !await TryProvision(user, ct))
        {
            return new("Tài khoản đang chờ phân công vào nhà hàng và chưa thể đăng nhập.", 202, true, Map(user));
        }

        return result.Status switch
        {
            201 => new("Tạo tài khoản thành công.", 201, true, Map(result.User!)),
            409 => new("Email hoặc tên đăng nhập đã tồn tại.", 409),
            400 => new("Vai trò không tồn tại.", 400),
            _ => Forbidden()
        };
    }

    public async Task<ResponseDTO> Update(Guid actorId, Guid userId, UpdateUserDTO request, CancellationToken ct)
    {
        if (!await CanManage(actorId, userId, ct))
        {
            return Forbidden();
        }

        var name = request.UserName?.Trim();
        var email = request.Email?.Trim();
        var fullName = request.FullName?.Trim();
        var phone = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
        var avatar = string.IsNullOrWhiteSpace(request.AvatarUrl) ? null : request.AvatarUrl.Trim();
        if (userId == Guid.Empty || string.IsNullOrEmpty(name) || name.Length > 100 || name.Any(char.IsWhiteSpace)
            || string.IsNullOrEmpty(email) || email.Length > 255
            || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email)
            || string.IsNullOrEmpty(fullName) || fullName.Length > 255 || phone?.Length > 20
            || avatar?.Length > 2048 || (avatar is not null
                && (!Uri.TryCreate(avatar, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))))
        {
            return new ResponseDTO("Thông tin tài khoản không hợp lệ.", 400);
        }

        return await unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            if (!await CanManage(actorId, userId, ct))
            {
                return Forbidden();
            }

            var user = await unitOfWork.UserRepo.FindUserAsync(userId, ct);
            if (user is null)
            {
                return Missing();
            }

            if (await unitOfWork.UserRepo.LoginExistsAsync(userId, name, email, ct))
            {
                return new ResponseDTO("Email hoặc tên đăng nhập đã được sử dụng.", 409);
            }

            var before = Map(user);
            user.UserName = name;
            user.Email = email;
            user.FullName = fullName;
            user.PhoneNumber = phone!;
            user.AvatarUrl = avatar!;
            user.UpdatedAt = DateTime.UtcNow;
            var result = Map(user);
            unitOfWork.RecordAudit(actorId, "UPDATE_USER", "User", userId.ToString(), before, result);
            return new ResponseDTO("Cập nhật thành công.", 200, true, result);
        }, ct);
    }

    public async Task<ResponseDTO> SetRole(Guid actorId, Guid userId, string roleId, CancellationToken ct)
    {
        if (!await CanManage(actorId, userId, ct))
        {
            return Forbidden();
        }

        if (string.IsNullOrWhiteSpace(roleId) || roleId.Trim().Length > 50)
        {
            return new("Vai trò không hợp lệ.", 400);
        }

        var managedRestaurant = await ManagedRestaurant(actorId, ct);
        return AccessResponse(await unitOfWork.ExecuteInTransactionAsync(() => unitOfWork.UserRepo.ChangeAccessAsync(actorId, userId, roleId.Trim(), null, settings.Value.AdminRoleIds, ct, managedRestaurant, StaffRoles), ct));
    }

    private static ResponseDTO AccessResponse((int Status, User? User) result)
    {
        return result.Status switch
        {
            200 => new("Cập nhật thành công.", 200, true, Map(result.User!)),
            404 => Missing(),
            403 => Forbidden(),
            409 => new("Tài khoản đang chờ phân công hoặc đây là quản trị nền tảng đang hoạt động cuối cùng.", 409),
            _ => new("Vai trò không tồn tại.", 400)
        };
    }

    private async Task FillRestaurantNames(IReadOnlyList<UserDTO> users, CancellationToken ct)
    {
        if (restaurantNames is null)
        {
            return;
        }

        var ids = users.Where(user => user.RestaurantId.HasValue).Select(user => user.RestaurantId!.Value).Distinct().ToArray();
        var names = await restaurantNames.GetAsync(ids, ct);
        foreach (var user in users)
        {
            user.RestaurantName = user.RestaurantId.HasValue ? names.GetValueOrDefault(user.RestaurantId.Value) : null;
        }
    }

    private static UserDTO Map(User user)
    {
        return new()
        {
            UserId = user.UserId,
            RestaurantId = user.RestaurantId,
            ProvisioningPending = user.ProvisioningPending,
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
