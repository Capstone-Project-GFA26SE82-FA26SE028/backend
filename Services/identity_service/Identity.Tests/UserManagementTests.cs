using Shared.Common.Pagination;
using Identity.DAL.UnitOfWorks;
using Identity.BLL.Implements;
using Identity.Common.DTOs;
using Identity.Common.Settings;
using Identity.DAL.Implements;
using Identity.Domain.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Identity.Tests;

public sealed partial class UserManagementTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly IdentityDbContext db;
    private readonly UserService service;
    private readonly RoleService roles;
    private readonly AuditLogService auditLogs;
    private readonly SessionService sessions;
    private readonly Guid adminId = Guid.NewGuid();
    private readonly Guid memberId = Guid.NewGuid();

    public UserManagementTests()
    {
        connection.Open();
        db = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        db.Roles.AddRange(new Role { RoleId = "admin", RoleName = "Admin" }, new Role { RoleId = "member", RoleName = "Member" });
        db.Users.AddRange(NewUser(adminId, "admin", "admin@example.test"), NewUser(memberId, "member", "member@example.test"));
        db.UserSessions.Add(new UserSession
        {
            UserSessionId = Guid.NewGuid(),
            UserId = memberId,
            LoggedInAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            RefreshTokenHash = new string('a', 64)
        });
        db.SaveChanges();
        db.ChangeTracker.Clear();
        service = new UserService(new UnitOfWork(db), Options.Create(new UserManagementSettings { AdminRoleIds = ["admin"] }));
        var settings = Options.Create(new UserManagementSettings { AdminRoleIds = ["admin"] });
        roles = new RoleService(new RoleRepository(db), new UnitOfWork(db), settings);
        auditLogs = new AuditLogService(new AuditLogRepository(db), new UserRepository(db), settings);
        sessions = new SessionService(new SessionRepository(db), new UserRepository(db), settings);
    }

    private static User NewUser(Guid id, string role, string email)
    {
        return new()
        {
            UserId = id,
            RoleId = role,
            UserName = role,
            Email = email,
            FullName = "Original name",
            PasswordHash = "never-return-this",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task MemberCannotReadListOrOtherUserOrChangeStatus()
    {
        Assert.Equal(403, (await service.GetAll(memberId, 1, 20, null, default)).StatusCode);
        Assert.Equal(403, (await service.GetById(memberId, adminId, default)).StatusCode);
        Assert.Equal(403, (await service.SetStatus(memberId, adminId, false, default)).StatusCode);
        Assert.True((await db.Users.AsNoTracking().SingleAsync(u => u.UserId == adminId)).IsActive);
    }
    [Fact]
    public async Task EmptyAdminConfigurationDeniesAdminOperations()
    {
        var closed = new UserService(new UnitOfWork(db), Options.Create(new UserManagementSettings()));
        Assert.Equal(403, (await closed.GetAll(adminId, 1, 20, null, default)).StatusCode);
    }
    [Fact]
    public async Task ProfileUpdateChangesOnlyAllowedFields()
    {
        var response = await service.UpdateMe(memberId, new UpdateProfileDTO
        {
            FullName = " Updated name ",
            PhoneNumber = "0123456789",
            AvatarUrl = "https://example.test/avatar.png"
        }, default);
        Assert.Equal(200, response.StatusCode);
        db.ChangeTracker.Clear();
        var user = await db.Users.SingleAsync(u => u.UserId == memberId);
        Assert.Equal("Updated name", user.FullName);
        Assert.Equal("member", user.RoleId);
        Assert.Equal("member@example.test", user.Email);
        Assert.Equal("never-return-this", user.PasswordHash);
        Assert.NotNull(user.UpdatedAt);
        Assert.Equal("Original name", (await db.Users.SingleAsync(u => u.UserId == adminId)).FullName);
        Assert.DoesNotContain("PasswordHash", System.Text.Json.JsonSerializer.Serialize(response));
    }
    [Fact]
    public async Task InvalidProfileIsRejectedWithoutSaving()
    {
        Assert.Equal(400, (await service.UpdateMe(memberId, new UpdateProfileDTO { FullName = " " }, default)).StatusCode);
        Assert.Equal(400, (await service.UpdateMe(memberId, new UpdateProfileDTO { FullName = "Name", AvatarUrl = "javascript:alert(1)" }, default)).StatusCode);
        Assert.Equal("Original name", (await db.Users.SingleAsync(u => u.UserId == memberId)).FullName);
    }
    [Fact]
    public async Task AdminCanSearchAndPageWithoutExposingPassword()
    {
        var response = await service.GetAll(adminId, 1, 1, "member@", default);
        var page = Assert.IsType<PagedResult<UserDTO>>(response.Result);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(memberId, Assert.Single(page.Items).UserId);
        Assert.Equal(400, (await service.GetAll(adminId, 0, 20, null, default)).StatusCode);
        Assert.Equal(400, (await service.GetAll(adminId, int.MaxValue, 100, null, default)).StatusCode);
    }
    [Fact]
    public async Task LockRevokesSessionAndUnlockDoesNotReactivateIt()
    {
        Assert.Equal(200, (await service.SetStatus(adminId, memberId, false, default)).StatusCode);
        Assert.False((await db.Users.AsNoTracking().SingleAsync(u => u.UserId == memberId)).IsActive);
        Assert.NotNull((await db.UserSessions.AsNoTracking().SingleAsync()).RevokedAt);
        Assert.Equal(200, (await service.SetStatus(adminId, memberId, true, default)).StatusCode);
        Assert.True((await db.Users.AsNoTracking().SingleAsync(u => u.UserId == memberId)).IsActive);
        var session = await db.UserSessions.AsNoTracking().SingleAsync();
        Assert.NotNull(session.RevokedAt);
        Assert.False(await new SessionRepository(db).IsActiveAsync(session.UserSessionId, memberId, "member", DateTime.UtcNow));
    }
    [Fact]
    public async Task AdminCannotLockSelfAndMissingUserReturns404()
    {
        Assert.Equal(400, (await service.SetStatus(adminId, adminId, false, default)).StatusCode);
        Assert.Equal(404, (await service.GetById(adminId, Guid.NewGuid(), default)).StatusCode);
        Assert.Equal(404, (await service.SetStatus(adminId, Guid.NewGuid(), false, default)).StatusCode);
    }

    private static CreateUserDTO Employee(string email = "new@example.test", string name = "new.employee", string role = "member", string password = "EmployeePass123!")
    {
        return new()
        {
            Email = email,
            UserName = name,
            FullName = "New Employee",
            RoleId = role,
            Password = password
        };
    }

    [Fact]
    public async Task AdminCreatesEmployeeWithHashedPasswordAndRole()
    {
        var response = await service.Create(adminId, Employee(), default);
        Assert.Equal(201, response.StatusCode);
        var dto = Assert.IsType<UserDTO>(response.Result);
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.UserId == dto.UserId);
        Assert.Equal("member", user.RoleId);
        Assert.True(user.IsActive);
        Assert.NotEqual("EmployeePass123!", user.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify("EmployeePass123!", user.PasswordHash));
        Assert.DoesNotContain("Password", System.Text.Json.JsonSerializer.Serialize(response));
    }
    [Fact]
    public async Task DuplicateEmailOrUsernameIsRejectedIgnoringCase()
    {
        Assert.Equal(409, (await service.Create(adminId, Employee(email: "MEMBER@example.test"), default)).StatusCode);
        Assert.Equal(409, (await service.Create(adminId, Employee(name: "MEMBER"), default)).StatusCode);
        Assert.Equal(2, await db.Users.CountAsync());
    }
    [Fact]
    public async Task InvalidRoleOrPasswordDoesNotCreateAccount()
    {
        Assert.Equal(400, (await service.Create(adminId, Employee(role: "missing"), default)).StatusCode);
        Assert.Equal(400, (await service.Create(adminId, Employee(password: "short"), default)).StatusCode);
        Assert.Equal(400, (await service.Create(adminId, Employee(password: new string('é', 40)), default)).StatusCode);
        Assert.Equal(2, await db.Users.CountAsync());
    }
    [Fact]
    public async Task MemberCannotCreateListRolesOrAssignRole()
    {
        Assert.Equal(403, (await service.Create(memberId, Employee(role: "admin"), default)).StatusCode);
        Assert.Equal(403, (await roles.GetAll(memberId, default)).StatusCode);
        Assert.Equal(403, (await service.SetRole(memberId, memberId, "admin", default)).StatusCode);
    }
    [Fact]
    public async Task RoleChangeRevokesSessionsButSameRoleDoesNot()
    {
        Assert.Equal(200, (await service.SetRole(adminId, memberId, "member", default)).StatusCode);
        Assert.Null((await db.UserSessions.AsNoTracking().SingleAsync()).RevokedAt);
        Assert.Equal(400, (await service.SetRole(adminId, memberId, "missing", default)).StatusCode);
        Assert.Equal(200, (await service.SetRole(adminId, memberId, "admin", default)).StatusCode);
        Assert.Equal("admin", (await db.Users.AsNoTracking().SingleAsync(u => u.UserId == memberId)).RoleId);
        Assert.NotNull((await db.UserSessions.AsNoTracking().SingleAsync()).RevokedAt);
    }
    [Fact]
    public async Task LastAdminCannotBeDemotedButCanWhenAnotherActiveAdminExists()
    {
        Assert.Equal(409, (await service.SetRole(adminId, adminId, "member", default)).StatusCode);
        var repository = new UserRepository(db);
        Assert.Equal(409, (await repository.ChangeAccessAsync(adminId, adminId, null, false, ["admin"], default)).Status);
        Assert.Equal(200, (await service.SetRole(adminId, memberId, "admin", default)).StatusCode);
        Assert.Equal(200, (await service.SetRole(adminId, adminId, "member", default)).StatusCode);
        Assert.Equal("member", (await db.Users.AsNoTracking().SingleAsync(u => u.UserId == adminId)).RoleId);
    }
    [Fact]
    public async Task AdminCanListExistingRoles()
    {
        var response = await roles.GetAll(adminId, default);
        Assert.Equal(200, response.StatusCode);
        Assert.Equal(2, Assert.IsType<List<RoleDTO>>(response.Result).Count);
    }
    [Fact]
    public async Task UnitOfWorkRollsBackAccessChangeAndSessionRevocationOnFailure()
    {
        var unitOfWork = new UnitOfWork(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.ExecuteInTransactionAsync<int>(async () =>
        {
            var result = await unitOfWork.UserRepo.ChangeAccessAsync(adminId, memberId, null, false, ["admin"], default);
            Assert.Equal(200, result.Status);
            throw new InvalidOperationException("Simulated failure before commit");
        }));
        Assert.True((await db.Users.AsNoTracking().SingleAsync(u => u.UserId == memberId)).IsActive);
        Assert.Null((await db.UserSessions.AsNoTracking().SingleAsync()).RevokedAt);
    }

    [Fact]
    public async Task ActivityListsRequireActiveAdmin()
    {
        Assert.Equal(403, (await auditLogs.GetAll(memberId, 1, 20, null, default)).StatusCode);
        Assert.Equal(403, (await sessions.GetAll(memberId, 1, 20, memberId, default)).StatusCode);
        var admin = await db.Users.SingleAsync(user => user.UserId == adminId);
        admin.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(403, (await sessions.GetAll(adminId, 1, 20, null, default)).StatusCode);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public async Task ActivityListsRejectInvalidPagination(int page, int pageSize)
    {
        Assert.Equal(400, (await auditLogs.GetAll(adminId, page, pageSize, null, default)).StatusCode);
        Assert.Equal(400, (await sessions.GetAll(adminId, page, pageSize, null, default)).StatusCode);
    }

    [Fact]
    public async Task SessionListFiltersAndOmitsTokenHash()
    {
        var response = await sessions.GetAll(adminId, 1, 20, memberId, default);
        Assert.Equal(200, response.StatusCode);
        var page = Assert.IsType<PagedResult<SessionDTO>>(response.Result);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(memberId, Assert.Single(page.Items).UserId);
        Assert.DoesNotContain("RefreshToken", System.Text.Json.JsonSerializer.Serialize(response));
        var empty = await sessions.GetAll(adminId, 1, 20, adminId, default);
        Assert.Empty(Assert.IsType<PagedResult<SessionDTO>>(empty.Result).Items);
        Assert.Equal(400, (await sessions.GetAll(adminId, 1, 20, Guid.Empty, default)).StatusCode);
    }

    [Fact]
    public async Task AuditLogsAreFilteredAndPagedNewestFirst()
    {
        var now = DateTime.UtcNow;
        for (var index = 0; index < 3; index++)
        {
            db.AuditLogs.Add(new AuditLog
            {
                AuditLogId = Guid.NewGuid(),
                UserId = memberId,
                Action = "Update",
                EntityName = "User",
                EntityId = memberId.ToString(),
                CreatedAt = now.AddMinutes(index)
            });
        }

        await db.SaveChangesAsync();
        var response = await auditLogs.GetAll(adminId, 2, 1, memberId, default);
        Assert.Equal(200, response.StatusCode);
        var page = Assert.IsType<PagedResult<AuditLogDTO>>(response.Result);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.Page);
        Assert.Equal(now.AddMinutes(1), Assert.Single(page.Items).CreatedAt);
        var empty = await auditLogs.GetAll(adminId, 1, 20, adminId, default);
        Assert.Empty(Assert.IsType<PagedResult<AuditLogDTO>>(empty.Result).Items);
    }

    [Theory]
    [InlineData("127.0.0.1", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)", "Desktop")]
    [InlineData("::1", "Mozilla/5.0 (iPhone) Mobile", "Mobile")]
    [InlineData("2001:db8::1", "Mozilla/5.0 (Linux; Android 13) AppleWebKit", "Tablet")]
    [InlineData("127.0.0.1", "Mozilla/5.0 (iPad)", "Tablet")]
    [InlineData(null, null, "Unknown")]
    [InlineData("invalid-ip", "curl/8.0", "Unknown")]
    public async Task LoginPersistsClientMetadata(string? ipAddress, string? userAgent, string deviceType)
    {
        var user = await db.Users.SingleAsync(item => item.UserId == memberId);
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword("TestPassword123!");
        await db.SaveChangesAsync();
        var auth = new AuthService(new UnitOfWork(db), new SessionRepository(db),
            Options.Create(new JwtSettingModel
            {
                SecretKey = new string('k', 64)
            }));

        var response = await auth.Login(new AuthDTO.LoginDTO
        {
            Email = user.Email,
            Password = "TestPassword123!",
            IpAddress = ipAddress,
            UserAgent = userAgent
        });

        Assert.Equal(200, response.StatusCode);
        var login = Assert.IsType<AuthDTO.LoginResponseDTO>(response.Result);
        var session = await db.UserSessions.AsNoTracking()
            .SingleAsync(item => item.UserSessionId == login.SessionId);
        var expectedIp = System.Net.IPAddress.TryParse(ipAddress, out var parsedIp)
            ? parsedIp.ToString() : null;
        Assert.Equal(expectedIp, session.IpAddress);
        Assert.Equal(userAgent, session.UserAgent);
        Assert.Equal(deviceType, session.DeviceType);
        Assert.Null(session.RevokedAt);
        var audit = await db.AuditLogs.AsNoTracking().SingleAsync();
        Assert.Equal(expectedIp, audit.IpAddress);

        var list = await sessions.GetAll(adminId, 1, 20, memberId, default);
        var page = Assert.IsType<PagedResult<SessionDTO>>(list.Result);
        var dto = Assert.Single(page.Items, item => item.UserSessionId == login.SessionId);
        Assert.Equal(expectedIp, dto.IpAddress);
        Assert.Equal(userAgent, dto.UserAgent);
        Assert.Equal(deviceType, dto.DeviceType);
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }
}
