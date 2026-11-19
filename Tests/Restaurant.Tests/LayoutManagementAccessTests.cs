using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Restaurant.BLL.Implements;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;
using Restaurant.Common.Exceptions;
using Restaurant.DAL.Implements;
using Restaurant.DAL.Models;
using Xunit;

namespace Restaurant.Tests;

public sealed class LayoutManagementAccessTests
{
    [Theory]
    [InlineData("platform_admin")]
    [InlineData("waiter")]
    [InlineData("cashier")]
    public async Task OtherRolesCannotManageLayout(string role)
    {
        var access = new LayoutManagementAccess(new Identity(role), null!);
        var error = await Assert.ThrowsAsync<ManagementException>(() => access.RequireManager(default));
        Assert.Equal(403, error.StatusCode);
    }

    [Theory]
    [InlineData("manager")]
    [InlineData("admin")]
    public async Task ManagerCanOnlyAccessActiveAssignments(string role)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var db = new RestaurantDbContext(new DbContextOptionsBuilder<RestaurantDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
        var identity = new Identity(role);
        var assigned = Guid.NewGuid();
        var revoked = Guid.NewGuid();
        db.RestaurantStaffs.AddRange(new RestaurantStaff
        {
            RestaurantId = assigned,
            UserIdRef = identity.UserId,
            IsActive = true
        }, new RestaurantStaff
        {
            RestaurantId = revoked,
            UserIdRef = identity.UserId,
            IsActive = false
        });
        await db.SaveChangesAsync();
        var access = new LayoutManagementAccess(identity, new RestaurantStaffRepository(db));
        await access.RequireManager(default);
        Assert.Equal(assigned, await access.RequireRestaurant(null, default));
        Assert.Equal(assigned, await access.RequireRestaurant(assigned, default));
        foreach (var denied in new[] { revoked, Guid.NewGuid() })
        {
            var error = await Assert.ThrowsAsync<ManagementException>(() => access.RequireRestaurant(denied, default));
            Assert.Equal(403, error.StatusCode);
        }
    }

    private sealed class Identity(string role) : IStaffIdentity
    {
        public Guid UserId { get; } = Guid.NewGuid();

        public Task<StaffActorDto> GetCurrentAsync(CancellationToken ct)
        {
            return Task.FromResult(new StaffActorDto
            {
                UserId = UserId,
                RoleId = role,
                IsAdministrator = role == "platform_admin"
            });
        }

        public Task<StaffActorDto> GetUserAsync(Guid userId, CancellationToken ct)
        {
            throw new NotSupportedException();
        }
    }
}
