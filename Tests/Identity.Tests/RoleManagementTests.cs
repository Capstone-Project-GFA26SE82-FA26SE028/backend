using Identity.Common.DTOs;
using Identity.DAL.Implements;
using Microsoft.EntityFrameworkCore;
using Shared.Common.Pagination;
using Xunit;

namespace Identity.Tests;

public sealed partial class UserManagementTests
{
    [Fact]
    public async Task FixedRolesCannotBeMutatedEvenByAdmin()
    {
        var request = new RoleDTO("cashier", "Cashier", null);
        Assert.Equal(409, (await roles.Create(adminId, request, default)).StatusCode);
        Assert.Equal(409, (await roles.Update(adminId, "member", request, default)).StatusCode);
        Assert.Equal(409, (await roles.Delete(adminId, "member", default)).StatusCode);
        Assert.Equal(409, (await roles.Delete(adminId, "missing", default)).StatusCode);
        Assert.Equal(2, await db.Roles.CountAsync());
        Assert.Equal("Member", (await db.Roles.SingleAsync(x => x.RoleId == "member")).RoleName);
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task MemberCannotManageRoles()
    {
        var request = new RoleDTO("cashier", "Cashier", null);
        Assert.Equal(403, (await roles.GetAll(memberId, default)).StatusCode);
        Assert.Equal(403, (await roles.GetById(memberId, "admin", default)).StatusCode);
        Assert.Equal(403, (await roles.Create(memberId, request, default)).StatusCode);
        Assert.Equal(403, (await roles.Update(memberId, "admin", request, default)).StatusCode);
        Assert.Equal(403, (await roles.Delete(memberId, "admin", default)).StatusCode);
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task AdminCanSearchFixedRoles()
    {
        var result = await roles.GetAll(adminId, default, " ADM ");
        Assert.Equal("admin", Assert.Single(Assert.IsType<List<RoleDTO>>(result.Result)).RoleId);
        Assert.Equal(400, (await roles.GetAll(adminId, default, new string('x', 51))).StatusCode);
    }

    private static UpdateUserDTO UpdateBody()
    {
        return new UpdateUserDTO
        {
            UserName = "updated",
            Email = "updated@example.test",
            FullName = "Updated employee",
            PhoneNumber = "0901234567",
            AvatarUrl = "https://example.test/avatar.png"
        };
    }

    [Fact]
    public async Task AdminUpdatePreservesAccessAndAuditsWithoutSecrets()
    {
        Assert.Equal(200, (await service.Update(adminId, memberId, UpdateBody(), default)).StatusCode);
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.UserId == memberId);
        Assert.Equal("updated", user.UserName);
        Assert.Equal("member", user.RoleId);
        Assert.True(user.IsActive);
        Assert.Equal("never-return-this", user.PasswordHash);
        var audit = await db.AuditLogs.SingleAsync(x => x.Action == "UPDATE_USER");
        Assert.Contains("updated@example.test", audit.NewValue);
        Assert.DoesNotContain("PasswordHash", audit.NewValue);
        Assert.DoesNotContain("never-return-this", audit.NewValue);
    }

    [Fact]
    public async Task AdminUpdateRejectsDuplicatesAndNonAdmin()
    {
        Assert.Equal(403, (await service.Update(memberId, memberId, UpdateBody(), default)).StatusCode);
        var duplicate = UpdateBody();
        duplicate.Email = "ADMIN@example.test";
        Assert.Equal(409, (await service.Update(adminId, memberId, duplicate, default)).StatusCode);
        duplicate.Email = "updated@example.test";
        duplicate.UserName = "admin@example.test";
        Assert.Equal(409, (await service.Update(adminId, memberId, duplicate, default)).StatusCode);
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task AuditFailureRollsBackAdminUpdate()
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_update_audit BEFORE INSERT ON AuditLog
            BEGIN SELECT RAISE(ABORT, 'Audit unavailable'); END;
            """);
        await Assert.ThrowsAsync<DbUpdateException>(() => service.Update(adminId, memberId, UpdateBody(), default));
        Assert.Equal("member@example.test", (await db.Users.AsNoTracking().SingleAsync(x => x.UserId == memberId)).Email);
    }

    [Fact]
    public async Task AccountFiltersCombineBeforePagination()
    {
        var result = await service.GetAll(adminId, 1, 1, "example", default, new UserFilter
        {
            RoleId = "member",
            IsActive = true
        });
        var page = Assert.IsType<PagedResult<UserDTO>>(result.Result);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(memberId, Assert.Single(page.Items).UserId);
        var empty = await service.GetAll(adminId, 1, 20, null, default, new UserFilter
        {
            IsActive = false
        });
        Assert.Empty(Assert.IsType<PagedResult<UserDTO>>(empty.Result).Items);
    }

    [Fact]
    public async Task SessionFiltersRespectAccountTimeAndStatus()
    {
        var now = DateTime.UtcNow;
        var row = await db.UserSessions.SingleAsync();
        row.DeviceType = "Mobile";
        await db.SaveChangesAsync();
        var filter = new SessionFilter
        {
            Search = "member@",
            RoleId = "member",
            DeviceType = "Mobile",
            Status = "active",
            FromUtc = now.AddDays(-1),
            ToUtc = now.AddDays(1)
        };
        var result = await sessions.GetAll(adminId, 1, 20, null, default, filter);
        Assert.Equal(memberId, Assert.Single(Assert.IsType<PagedResult<SessionDTO>>(result.Result).Items).UserId);
        filter.Status = "revoked";
        Assert.Empty(Assert.IsType<PagedResult<SessionDTO>>((await sessions.GetAll(adminId, 1, 20, null, default, filter)).Result).Items);
        await service.SetStatus(adminId, memberId, false, default);
        Assert.Single(Assert.IsType<PagedResult<SessionDTO>>((await sessions.GetAll(adminId, 1, 20, null, default, filter)).Result).Items);
        filter.Status = "unknown";
        Assert.Equal(400, (await sessions.GetAll(adminId, 1, 20, null, default, filter)).StatusCode);
        filter.Status = "active";
        filter.FromUtc = now.AddDays(2);
        Assert.Equal(400, (await sessions.GetAll(adminId, 1, 20, null, default, filter)).StatusCode);
    }

    [Fact]
    public async Task LoginAcceptsUsernameAndRejectsAmbiguousIdentifiers()
    {
        var auth = await PrepareAuth();
        var result = await auth.Login(new AuthDTO.LoginDTO
        {
            Email = " MEMBER ",
            Password = "CurrentPassword123!"
        });
        Assert.Equal(200, result.StatusCode);
        var admin = await db.Users.SingleAsync(x => x.UserId == adminId);
        admin.UserName = "member@example.test";
        await db.SaveChangesAsync();
        Assert.Null(await new UserRepository(db).FindByEmailAsync("member@example.test"));
    }
}
