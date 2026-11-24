using Identity.BLL.Implements;
using Identity.Common.DTOs;
using Identity.Common.Settings;
using Identity.DAL.UnitOfWorks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Common.Images;
using Xunit;

namespace Identity.Tests;

public sealed partial class UserManagementTests
{
    private static readonly byte[] AvatarPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a8ioAAAAASUVORK5CYII=");

    private sealed class AvatarStorage : IImageStorage
    {
        public int Calls { get; private set; }
        public string? PublicId { get; private set; }
        public Func<Task>? BeforeReturn { get; set; }
        public int? FailureStatus { get; set; }
        public const string Url = "https://res.cloudinary.com/test/image/upload/identity/avatars/test.png";

        public async Task<string> UploadAsync(byte[] image, string publicId, CancellationToken ct)
        {
            Calls++;
            PublicId = publicId;
            if (BeforeReturn is not null)
            {
                await BeforeReturn();
            }

            if (FailureStatus.HasValue)
            {
                throw new ImageStorageException(FailureStatus.Value, "Cloudinary rejected the image.");
            }

            return Url;
        }

        public Task<byte[]> ReadAsync(string url, CancellationToken ct)
        {
            throw new NotSupportedException();
        }
    }

    private UserService AvatarService(AvatarStorage storage)
    {
        return new UserService(new UnitOfWork(db), Options.Create(new UserManagementSettings
        {
            AdminRoleIds = ["admin"]
        }), images: storage);
    }

    [Fact]
    public async Task AvatarUploadPersistsCloudUrlAndAuditsWithoutChangingAccountFields()
    {
        var storage = new AvatarStorage();
        var result = await AvatarService(storage).UploadAvatar(memberId, memberId, AvatarPng, default);
        Assert.Equal(200, result.StatusCode);
        Assert.StartsWith($"identity/avatars/{memberId:N}/", storage.PublicId);
        db.ChangeTracker.Clear();
        var user = await db.Users.SingleAsync(user => user.UserId == memberId);
        Assert.Equal(AvatarStorage.Url, user.AvatarUrl);
        Assert.Equal("member", user.RoleId);
        Assert.Equal("never-return-this", user.PasswordHash);
        Assert.Equal(AvatarStorage.Url, Assert.IsType<UserDTO>(result.Result).AvatarUrl);
        var audit = await db.AuditLogs.SingleAsync(log => log.Action == "UPDATE_AVATAR");
        Assert.Equal(memberId, audit.UserId);
        Assert.DoesNotContain(user.PasswordHash, audit.NewValue);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(503)]
    public async Task FailedCloudUploadPreservesOldAvatarAndCreatesNoAudit(int status)
    {
        var user = await db.Users.SingleAsync(user => user.UserId == memberId);
        user.AvatarUrl = "https://legacy.test/old.png";
        await db.SaveChangesAsync();
        var storage = new AvatarStorage { FailureStatus = status };
        var result = await AvatarService(storage).UploadAvatar(memberId, memberId, AvatarPng, default);
        Assert.Equal(status, result.StatusCode);
        db.ChangeTracker.Clear();
        Assert.Equal("https://legacy.test/old.png", (await db.Users.SingleAsync(user => user.UserId == memberId)).AvatarUrl);
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(2097153)]
    public async Task InvalidAvatarBytesAreRejectedBeforeCallingCloud(int size)
    {
        var storage = new AvatarStorage();
        var result = await AvatarService(storage).UploadAvatar(memberId, memberId, new byte[size], default);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public async Task ImageAtSizeLimitReachesCloudValidation()
    {
        var storage = new AvatarStorage();
        var bytes = new byte[CloudinaryImageStorage.MaxImageBytes];
        AvatarPng.CopyTo(bytes, 0);
        Assert.Equal(200, (await AvatarService(storage).UploadAvatar(memberId, memberId, bytes, default)).StatusCode);
        Assert.Equal(1, storage.Calls);
    }

    [Fact]
    public async Task MissingCloudCredentialsReturns503AndPreservesAvatar()
    {
        using var http = new HttpClient();
        var actual = new UserService(new UnitOfWork(db), Options.Create(new UserManagementSettings
        {
            AdminRoleIds = ["admin"]
        }), images: new CloudinaryImageStorage(http, new CloudinarySettings()));
        Assert.Equal(503, (await actual.UploadAvatar(memberId, memberId, AvatarPng, default)).StatusCode);
        Assert.Null((await db.Users.AsNoTracking().SingleAsync(user => user.UserId == memberId)).AvatarUrl);
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task MemberCannotUploadAnotherUsersAvatar()
    {
        var storage = new AvatarStorage();
        Assert.Equal(403, (await AvatarService(storage).UploadAvatar(memberId, adminId, AvatarPng, default)).StatusCode);
        Assert.Equal(0, storage.Calls);
    }

    [Fact]
    public async Task RestaurantAdminCannotUploadForeignStaffAvatar()
    {
        var setup = await RestaurantAccounts();
        var storage = new AvatarStorage();
        var actual = new UserService(new UnitOfWork(db), Options.Create(new UserManagementSettings
        {
            AdminRoleIds = ["platform_admin"]
        }), setup.Access, images: storage);
        Assert.Equal(200, (await actual.UploadAvatar(adminId, memberId, AvatarPng, default)).StatusCode);
        var foreignRestaurant = Guid.NewGuid();
        await db.Users.Where(user => user.UserId == memberId).ExecuteUpdateAsync(
            setters => setters.SetProperty(user => user.RestaurantId, foreignRestaurant));
        Assert.Equal(403, (await actual.UploadAvatar(adminId, memberId, AvatarPng, default)).StatusCode);
        Assert.Equal(1, storage.Calls);
    }

    [Fact]
    public async Task UserDisabledDuringUploadCannotSaveAvatar()
    {
        var storage = new AvatarStorage
        {
            BeforeReturn = async () => await db.Users.Where(user => user.UserId == memberId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.IsActive, false))
        };
        Assert.Equal(403, (await AvatarService(storage).UploadAvatar(memberId, memberId, AvatarPng, default)).StatusCode);
        Assert.Null((await db.Users.AsNoTracking().SingleAsync(user => user.UserId == memberId)).AvatarUrl);
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task JsonProfileRejectsNewUrlButPreservesLegacyAvatarWhenOmitted()
    {
        var user = await db.Users.SingleAsync(user => user.UserId == memberId);
        user.AvatarUrl = "https://legacy.test/old.png";
        await db.SaveChangesAsync();
        Assert.Equal(400, (await service.UpdateMe(memberId, new UpdateProfileDTO
        {
            FullName = "New name",
            AvatarUrl = AvatarStorage.Url
        }, default)).StatusCode);
        Assert.Equal(200, (await service.UpdateMe(memberId, new UpdateProfileDTO
        {
            FullName = "New name"
        }, default)).StatusCode);
        Assert.Equal("https://legacy.test/old.png", user.AvatarUrl);
        Assert.Equal(200, (await service.UpdateMe(memberId, new UpdateProfileDTO
        {
            FullName = "New name",
            AvatarUrl = ""
        }, default)).StatusCode);
        Assert.Null(user.AvatarUrl);
    }

    [Fact]
    public async Task AdminJsonCreateAndUpdateCannotBypassAvatarUpload()
    {
        var update = UpdateBody();
        update.AvatarUrl = "https://external.test/avatar.png";
        Assert.Equal(400, (await service.Update(adminId, memberId, update, default)).StatusCode);
        Assert.Equal(400, (await service.Create(adminId, new CreateUserDTO
        {
            UserName = "newuser",
            Email = "newuser@example.test",
            Password = "Password123!",
            FullName = "New User",
            RoleId = "member",
            AvatarUrl = AvatarStorage.Url
        }, default)).StatusCode);
        Assert.Equal(2, await db.Users.CountAsync());
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }
}
