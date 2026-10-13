using Identity.Common.DTOs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Identity.Tests;

public sealed partial class UserManagementTests
{
    [Fact]
    public async Task AdminCanManageUnusedRoleWithAudits()
    {
        db.AuditIpAddress = "::1";
        var created = await roles.Create(adminId, new RoleDTO("cashier", "Cashier", "Initial"), default);
        Assert.Equal(201, created.StatusCode);
        Assert.Equal("cashier", Assert.IsType<RoleDTO>(created.Result).RoleId);
        Assert.Equal(200, (await roles.GetById(adminId, "cashier", default)).StatusCode);
        Assert.Equal(200, (await roles.Update(adminId, "cashier",
            new RoleDTO("cashier", "Updated cashier", null), default)).StatusCode);
        var role = await db.Roles.AsNoTracking().SingleAsync(item => item.RoleId == "cashier");
        Assert.Equal("Updated cashier", role.RoleName);
        Assert.Null(role.Description);
        Assert.Equal(200, (await roles.Delete(adminId, "cashier", default)).StatusCode);
        Assert.Equal(404, (await roles.GetById(adminId, "cashier", default)).StatusCode);
        var logs = await db.AuditLogs.AsNoTracking().Where(item => item.EntityName == "Role").ToListAsync();
        Assert.Equal(3, logs.Count);
        Assert.All(logs, item =>
        {
            Assert.Equal(adminId, item.UserId);
            Assert.Equal("::1", item.IpAddress);
            Assert.Equal("cashier", item.EntityId);
        });
        var update = Assert.Single(logs, item => item.Action == "UPDATE_ROLE");
        Assert.Contains("Cashier", update.OldValue);
        Assert.Contains("Updated cashier", update.NewValue);
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
        Assert.Equal(2, await db.Roles.CountAsync());
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task UsedAndAdminRolesCannotBeDeleted()
    {
        Assert.Equal(409, (await roles.Delete(adminId, "admin", default)).StatusCode);
        Assert.Equal(409, (await roles.Delete(adminId, "member", default)).StatusCode);
        var member = await db.Users.SingleAsync(item => item.UserId == memberId);
        member.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Equal(409, (await roles.Delete(adminId, "member", default)).StatusCode);
        Assert.Equal(404, (await roles.Delete(adminId, "missing", default)).StatusCode);
        Assert.Equal(2, await db.Roles.CountAsync());
    }

    [Fact]
    public async Task RoleValidationPreventsDuplicatesAndIdChanges()
    {
        Assert.Equal(409, (await roles.Create(adminId, new RoleDTO("admin", "Duplicate", null), default)).StatusCode);
        Assert.Equal(400, (await roles.Create(adminId, new RoleDTO("bad/id", "Name", null), default)).StatusCode);
        Assert.Equal(400, (await roles.Create(adminId, new RoleDTO("cashier", " ", null), default)).StatusCode);
        Assert.Equal(400, (await roles.Create(adminId, new RoleDTO("cashier", new string('x', 51), null), default)).StatusCode);
        Assert.Equal(400, (await roles.Create(adminId, new RoleDTO("cashier", "Name", new string('x', 2049)), default)).StatusCode);
        Assert.Equal(400, (await roles.Update(adminId, "member", new RoleDTO("other", "Member", null), default)).StatusCode);
        Assert.Equal(404, (await roles.Update(adminId, "missing", new RoleDTO("missing", "Name", null), default)).StatusCode);
        Assert.Equal(2, await db.Roles.CountAsync());
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task AuditFailureRollsBackRoleCreation()
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_role_audit BEFORE INSERT ON AuditLog
            BEGIN SELECT RAISE(ABORT, 'Audit unavailable'); END;
            """);
        await Assert.ThrowsAsync<DbUpdateException>(() => roles.Create(adminId,
            new RoleDTO("cashier", "Cashier", null), default));
        Assert.False(await db.Roles.AsNoTracking().AnyAsync(item => item.RoleId == "cashier"));
        Assert.Empty(await db.AuditLogs.AsNoTracking().ToListAsync());
    }
}
