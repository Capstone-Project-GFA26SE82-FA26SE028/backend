using System.Security.Cryptography;
using System.Text.Json;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;
using Restaurant.Common.Exceptions;
using Restaurant.DAL.Interfaces;
using Restaurant.DAL.Models;
using Shared.Common.Images;

namespace Restaurant.BLL.Implements;

public sealed class RestaurantRegistrationService(IRestaurantRegistrationRepository registrations,
    IRestaurantStaffRepository staff, IManagementTransaction transaction, RestaurantService restaurants,
    IStaffIdentity identity, IRestaurantApplicantClient applicants, IRestaurantRegistrationDocuments documents,
    TimeProvider clock)
{
    public async Task<RestaurantRegistration> Create(Guid id, RestaurantWriteDto restaurant,
        RestaurantApplicantCredentials credentials, byte[] businessImage, byte[] safetyImage, CancellationToken ct)
    {
        RestaurantService.ValidateRegistration(restaurant);
        ValidateDocument(businessImage);
        ValidateDocument(safetyImage);
        var applicant = await applicants.Reserve(id, credentials, ct);
        var existing = await registrations.GetForApplicantAsync(applicant.UserId, ct);
        if (existing is not null)
        {
            return MatchRegistration(existing, id);
        }

        var businessAsset = await documents.UploadAsync(businessImage, DocumentPath(id, "business", businessImage), ct);
        var safetyAsset = await documents.UploadAsync(safetyImage, DocumentPath(id, "food-safety", safetyImage), ct);
        var registration = new RestaurantRegistration
        {
            RegistrationId = id,
            ApplicantUserRef = applicant.UserId,
            NewAccount = applicant.NewAccount,
            Email = applicant.Email,
            RestaurantDetails = JsonSerializer.Serialize(restaurant),
            BusinessCertificateAssetId = businessAsset,
            FoodSafetyCertificateAssetId = safetyAsset,
            SubmittedAt = clock.GetUtcNow().UtcDateTime
        };
        return await transaction.ExecuteAsync(async () =>
        {
            var current = await registrations.GetForApplicantAsync(applicant.UserId, ct);
            if (current is not null)
            {
                return MatchRegistration(current, id);
            }

            registrations.Create(registration);
            return registration;
        }, ct);
    }

    public async Task<(List<RestaurantRegistration> Items, int TotalCount)> GetAll(string? status, int page, int pageSize, CancellationToken ct)
    {
        await RequirePlatformAdmin(ct);
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue
            || status is not (null or "Pending" or "Provisioning" or "Approved"))
        {
            throw new ManagementException(400, "Phân trang hoặc trạng thái hồ sơ không hợp lệ.");
        }

        return await registrations.GetAllAsync(status, page, pageSize, ct);
    }

    public async Task<RestaurantRegistration> GetById(Guid id, CancellationToken ct)
    {
        await RequirePlatformAdmin(ct);
        return await Find(id, ct);
    }

    public async Task<(byte[] Bytes, string ContentType)> GetDocument(Guid id, string kind, CancellationToken ct)
    {
        var registration = await GetById(id, ct);
        var asset = kind switch
        {
            "business" => registration.BusinessCertificateAssetId,
            "food-safety" => registration.FoodSafetyCertificateAssetId,
            _ => throw new ManagementException(400, "Loại giấy tờ không hợp lệ.")
        };
        var image = await documents.ReadAsync(asset, ct);
        return (image, ValidateDocument(image));
    }

    public async Task<RestaurantRegistration> Approve(Guid id, CancellationToken ct)
    {
        var reviewer = await RequirePlatformAdmin(ct);
        var registration = await transaction.ExecuteAsync(async () =>
        {
            var current = await Find(id, ct);
            if (current.Status != "Pending")
            {
                return current;
            }

            if ((await staff.LockUserAssignmentsAsync(current.ApplicantUserRef, ct)).Any(item => item.IsActive))
            {
                throw new ManagementException(409, "Người nộp hồ sơ đã được phân công tại nhà hàng khác.");
            }

            var restaurantId = Guid.NewGuid();
            restaurants.CreateFromRegistration(restaurantId, Details(current));
            await staff.AddAsync(new RestaurantStaff
            {
                RestaurantId = restaurantId,
                UserIdRef = current.ApplicantUserRef,
                IsActive = true
            });
            current.RestaurantId = restaurantId;
            current.ReviewedAt = clock.GetUtcNow().UtcDateTime;
            current.ReviewedByRef = reviewer;
            current.Status = "Provisioning";
            return current;
        }, ct);

        if (registration.Status == "Approved")
        {
            return registration;
        }

        await applicants.Activate(id, registration.ApplicantUserRef, registration.RestaurantId!.Value,
            registration.NewAccount, ct);
        return await transaction.ExecuteAsync(() =>
        {
            registration.Status = "Approved";
            return Task.FromResult(registration);
        }, ct);
    }

    public static RestaurantWriteDto Details(RestaurantRegistration registration)
    {
        return JsonSerializer.Deserialize<RestaurantWriteDto>(registration.RestaurantDetails)
            ?? throw new InvalidOperationException("Thiếu dữ liệu hồ sơ nhà hàng.");
    }

    private async Task<Guid> RequirePlatformAdmin(CancellationToken ct)
    {
        var actor = await identity.GetCurrentAsync(ct);
        if (!actor.IsAdministrator || actor.RoleId != "platform_admin")
        {
            throw new ManagementException(403, "Chỉ quản trị viên nền tảng được xem và duyệt hồ sơ.");
        }

        return actor.UserId;
    }

    private async Task<RestaurantRegistration> Find(Guid id, CancellationToken ct)
    {
        return await registrations.GetByIdAsync(id, ct)
            ?? throw new ManagementException(404, "Không tìm thấy hồ sơ đăng ký nhà hàng.");
    }

    private static RestaurantRegistration MatchRegistration(RestaurantRegistration registration, Guid id)
    {
        if (registration.RegistrationId != id)
        {
            throw new ManagementException(409, "Tài khoản này đã có hồ sơ đăng ký nhà hàng.");
        }

        return registration;
    }

    private static string DocumentPath(Guid id, string kind, byte[] contents)
    {
        return $"restaurant/registrations/{id:N}/{kind}-{Convert.ToHexString(SHA256.HashData(contents)).ToLowerInvariant()}";
    }

    private static string ValidateDocument(byte[] image)
    {
        if (image.Length > 0 && image.Length <= CloudinaryImageStorage.MaxImageBytes)
        {
            ReadOnlySpan<byte> bytes = image;
            if (bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            {
                return "image/png";
            }

            if (bytes.StartsWith(new byte[] { 255, 216, 255 }))
            {
                return "image/jpeg";
            }

            if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
            {
                return "image/webp";
            }
        }

        throw new ManagementException(400, "Mỗi giấy tờ cần một ảnh JPEG, PNG hoặc WebP, tối đa 2 MiB.");
    }
}
