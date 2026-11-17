using Identity.BLL.Interfaces;
using Identity.BLL.Implements;
using Identity.Common.DTOs;
using Identity.Common.Settings;
using Identity.DAL.UnitOfWorks;
using Identity.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shared.Common.Pagination;
using Xunit;

namespace Identity.Tests;

public sealed partial class UserManagementTests
{
    private sealed class RestaurantAccess : IUserRestaurantAccess, IStaffProvisioner
    {
        public bool Allowed { get; set; } = true;
        public bool Available { get; set; } = true;
        public bool IsConfigured => true;
        public List<(Guid Restaurant, Guid User)> Assignments { get; } = [];

        public Task<bool> CanManageAsync(Guid actorId, Guid restaurantId, CancellationToken ct)
        {
            return Task.FromResult(Allowed);
        }

        public Task<bool> EnsureAssignmentAsync(Guid restaurantId, Guid userId, CancellationToken ct)
        {
            if (Available)
            {
                Assignments.Add((restaurantId, userId));
            }

            return Task.FromResult(Available);
        }
    }

    private async Task<(UserService Service, RestaurantAccess Access, Guid Restaurant)> RestaurantAccounts()
    {
        var restaurant = Guid.NewGuid();
        db.Roles.AddRange(new Role { RoleId = "waiter", RoleName = "Waiter" },
            new Role { RoleId = "cashier", RoleName = "Cashier" });
        var admin = await db.Users.SingleAsync(user => user.UserId == adminId);
        admin.RestaurantId = restaurant;
        var member = await db.Users.SingleAsync(user => user.UserId == memberId);
        member.RestaurantId = restaurant;
        member.RoleId = "waiter";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var access = new RestaurantAccess();
        return (new UserService(new UnitOfWork(db), Options.Create(new UserManagementSettings
        {
            AdminRoleIds = ["platform_admin"]
        }), access, access), access, restaurant);
    }

    [Fact]
    public async Task RestaurantAdminListsOnlyOwnStaffAndCannotReadOrModifyForeignStaff()
    {
        var setup = await RestaurantAccounts();
        var foreign = NewUser(Guid.NewGuid(), "waiter", "foreign@example.test");
        foreign.RestaurantId = Guid.NewGuid();
        db.Users.Add(foreign);
        await db.SaveChangesAsync();
        var list = await setup.Service.GetAll(adminId, 1, 20, null, default);
        var page = Assert.IsType<PagedResult<UserDTO>>(list.Result);
        Assert.Equal(memberId, Assert.Single(page.Items).UserId);
        Assert.Equal(403, (await setup.Service.GetById(adminId, foreign.UserId, default)).StatusCode);
        Assert.Equal(403, (await setup.Service.SetStatus(adminId, foreign.UserId, false, default)).StatusCode);
        Assert.Equal(403, (await setup.Service.SetRole(adminId, foreign.UserId, "cashier", default)).StatusCode);
        Assert.Equal(403, (await setup.Service.Update(adminId, foreign.UserId, new UpdateUserDTO(), default)).StatusCode);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("platform_admin")]
    [InlineData("customer")]
    public async Task RestaurantAdminCannotElevateStaff(string role)
    {
        var setup = await RestaurantAccounts();
        Assert.Equal(403, (await setup.Service.SetRole(adminId, memberId, role, default)).StatusCode);
        Assert.Equal(403, (await setup.Service.Create(adminId, StaffRequest(role), default)).StatusCode);
    }

    [Fact]
    public async Task RestaurantAdminChangesOwnStaffRoleAndRevokesSessions()
    {
        var setup = await RestaurantAccounts();
        Assert.Equal(200, (await setup.Service.SetRole(adminId, memberId, "cashier", default)).StatusCode);
        Assert.All(await db.UserSessions.AsNoTracking().ToListAsync(), session => Assert.NotNull(session.RevokedAt));
        Assert.Equal(200, (await setup.Service.SetStatus(adminId, memberId, false, default)).StatusCode);
        Assert.False((await db.Users.AsNoTracking().SingleAsync(user => user.UserId == memberId)).IsActive);
        setup.Access.Allowed = false;
        Assert.Equal(403, (await setup.Service.GetAll(adminId, 1, 20, null, default)).StatusCode);
    }

    [Fact]
    public async Task CreationAssignsOwnerAndRetriesPendingProvisioningWithoutActivatingEarly()
    {
        var setup = await RestaurantAccounts();
        setup.Access.Available = false;
        var response = await setup.Service.Create(adminId, StaffRequest("waiter"), default);
        Assert.Equal(202, response.StatusCode);
        var user = Assert.IsType<UserDTO>(response.Result);
        Assert.Equal(setup.Restaurant, user.RestaurantId);
        Assert.True(user.ProvisioningPending);
        Assert.False(user.IsActive);
        Assert.Equal(409, (await setup.Service.SetStatus(adminId, user.UserId, true, default)).StatusCode);
        setup.Access.Available = true;
        await setup.Service.ProvisionPending(default);
        await setup.Service.ProvisionPending(default);
        Assert.Single(setup.Access.Assignments);
        var saved = await db.Users.AsNoTracking().SingleAsync(value => value.UserId == user.UserId);
        Assert.True(saved.IsActive);
        Assert.False(saved.ProvisioningPending);
    }

    [Fact]
    public async Task CreationRejectsForeignRestaurantAndActivatesAfterSuccessfulAssignment()
    {
        var setup = await RestaurantAccounts();
        var request = StaffRequest("waiter");
        request.RestaurantId = Guid.NewGuid();
        Assert.Equal(403, (await setup.Service.Create(adminId, request, default)).StatusCode);
        request.RestaurantId = null;
        var result = await setup.Service.Create(adminId, request, default);
        Assert.Equal(201, result.StatusCode);
        var user = Assert.IsType<UserDTO>(result.Result);
        Assert.True(user.IsActive);
        Assert.False(user.ProvisioningPending);
        Assert.Equal((setup.Restaurant, user.UserId), Assert.Single(setup.Access.Assignments));
    }

    private static CreateUserDTO StaffRequest(string role)
    {
        return new CreateUserDTO
        {
            UserName = "new-staff",
            Email = "new-staff@example.test",
            FullName = "Nhân viên",
            Password = "Staff-password-123",
            RoleId = role
        };
    }
}
