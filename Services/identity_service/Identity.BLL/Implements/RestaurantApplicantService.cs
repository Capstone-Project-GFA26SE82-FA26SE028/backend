using System.Data;
using System.Net.Mail;
using System.Text;
using Identity.BLL.Interfaces;
using Identity.DAL.Interfaces;
using Identity.DAL.UnitOfWorks;
using Identity.Domain.Models;

namespace Identity.BLL.Implements;

public sealed class RestaurantApplicantService(IUnitOfWork work, ISessionRepository sessions) : IRestaurantApplicantService
{
    public async Task<RestaurantApplicant> Reserve(Guid registrationId, Guid? currentUserId, string email, string password,
        string fullName, string phone, CancellationToken ct)
    {
        if (currentUserId.HasValue)
        {
            var current = await work.UserRepo.FindUserAsync(currentUserId.Value, ct);
            if (current is null || !current.IsActive || current.RoleId != "customer" || current.RestaurantId.HasValue)
            {
                throw Failure(403, "Chỉ tài khoản khách hàng đang hoạt động mới được dùng để nộp hồ sơ.");
            }

            return new RestaurantApplicant(current.UserId, current.Email);
        }

        email = email.Trim().ToLowerInvariant();
        fullName = fullName.Trim();
        phone = phone.Trim();
        if (email.Length > 255 || !MailAddress.TryCreate(email, out var address) || address.Address != email
            || string.IsNullOrWhiteSpace(password) || password.Length < 8 || Encoding.UTF8.GetByteCount(password) > 72
            || fullName.Length is < 1 or > 255 || phone.Length > 20)
        {
            throw Failure(400, "Thông tin tài khoản mới không hợp lệ.");
        }

        return await work.ExecuteInTransactionAsync(async () =>
        {
            var pending = await work.UserRepo.LockAsync(registrationId, ct);
            if (pending is not null)
            {
                if (pending.UserName != AccountName(registrationId) || pending.Email != email
                    || !BCrypt.Net.BCrypt.Verify(password, pending.PasswordHash))
                {
                    throw Failure(409, "Thông tin tài khoản hoặc mã đăng ký đã được sử dụng.");
                }

                return new RestaurantApplicant(pending.UserId, pending.Email);
            }

            var user = new User
            {
                UserId = registrationId,
                UserName = AccountName(registrationId),
                Email = email,
                FullName = fullName,
                PhoneNumber = phone,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                IsActive = false,
                ProvisioningPending = true,
                CreatedAt = DateTime.UtcNow
            };
            if (!await work.UserRepo.CreateCustomerAsync(user, ct))
            {
                throw Failure(409, "Email đã được sử dụng. Hãy đăng nhập nếu đây là tài khoản khách của bạn.");
            }

            return new RestaurantApplicant(user.UserId, user.Email);
        }, IsolationLevel.Serializable, ct);
    }

    public Task<RestaurantApplicant> Activate(Guid registrationId, Guid userId, Guid restaurantId, bool newAccount,
        Guid reviewerId, CancellationToken ct)
    {
        return work.ExecuteInTransactionAsync(async () =>
        {
            var reviewer = await work.UserRepo.FindUserAsync(reviewerId, ct);
            if (reviewer is null || !reviewer.IsActive || reviewer.RoleId != "platform_admin")
            {
                throw Failure(403, "Chỉ quản trị viên nền tảng được duyệt hồ sơ.");
            }

            var user = await work.UserRepo.LockAsync(userId, ct);
            if (user is null)
            {
                throw Failure(404, "Không tìm thấy tài khoản đã nộp hồ sơ.");
            }

            if (user.RoleId == "admin" && user.RestaurantId == restaurantId && user.IsActive)
            {
                return new RestaurantApplicant(user.UserId, user.Email);
            }

            if (user.RoleId != "customer" || user.RestaurantId.HasValue
                || (newAccount && (user.UserId != registrationId || user.UserName != AccountName(registrationId)
                    || user.IsActive || !user.ProvisioningPending))
                || (!newAccount && (!user.IsActive || user.ProvisioningPending)))
            {
                throw Failure(409, "Tài khoản đã thay đổi và không còn đủ điều kiện nhận quyền quản trị nhà hàng.");
            }

            user.RoleId = "admin";
            user.RestaurantId = restaurantId;
            user.IsActive = true;
            user.ProvisioningPending = false;
            user.UpdatedAt = DateTime.UtcNow;
            await sessions.RevokeForTransferAsync(userId, reviewerId, ct);
            work.RecordAudit(reviewerId, "APPROVE_RESTAURANT_APPLICANT", "User", userId.ToString(),
                new { RoleId = "customer" }, new { user.RoleId, RestaurantId = restaurantId, RegistrationId = registrationId });
            return new RestaurantApplicant(user.UserId, user.Email);
        }, IsolationLevel.Serializable, ct);
    }

    private static string AccountName(Guid registrationId)
    {
        return $"r-{registrationId:N}";
    }

    private static RestaurantApplicantException Failure(int status, string message)
    {
        return new RestaurantApplicantException(status, message);
    }
}
