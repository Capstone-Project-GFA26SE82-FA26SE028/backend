using Identity.BLL.Implements;
using Identity.BLL.Interfaces;
using Identity.Common.DTOs;
using Identity.Common.Settings;
using Identity.DAL.UnitOfWorks;
using Identity.Domain.Models;
using Microsoft.Extensions.Options;
using Shared.Common.Pagination;
using Xunit;

namespace Identity.Tests;

public sealed partial class UserManagementTests
{
    [Theory]
    [InlineData("Nguyen")]
    [InlineData("staff.login")]
    [InlineData("staff@example.test")]
    public async Task AuditFiltersActorActionAndDateBeforePaging(string search)
    {
        var user = await db.Users.FindAsync(memberId);
        user!.FullName = "Nguyen Van A";
        user.UserName = "staff.login";
        user.Email = "staff@example.test";
        user.AvatarUrl = "https://example.test/avatar.png";
        var date = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
        for (var index = 0; index < 4; index++)
        {
            db.AuditLogs.Add(new AuditLog
            {
                AuditLogId = Guid.NewGuid(),
                UserId = memberId,
                EntityName = "User",
                Action = index == 3 ? "LOGIN" : "UPDATE_USER",
                CreatedAt = index == 2 ? date.AddDays(-3) : date.AddMinutes(index)
            });
        }

        await db.SaveChangesAsync();
        var result = await auditLogs.GetAll(adminId, 2, 1, null, default, new AuditLogFilter
        {
            Search = search,
            Action = "UPDATE_USER",
            FromUtc = date.Date,
            ToUtc = date.AddDays(1)
        });
        var page = Assert.IsType<PagedResult<AuditLogDTO>>(result.Result);
        Assert.Equal(2, page.TotalCount);
        var item = Assert.Single(page.Items);
        Assert.Equal("Nguyen Van A", item.FullName);
        Assert.Equal("staff.login", item.UserName);
        Assert.Equal("staff@example.test", item.Email);
        Assert.Equal(user.AvatarUrl, item.AvatarUrl);
        Assert.Equal(400, (await auditLogs.GetAll(adminId, 1, 20, null, default,
            new AuditLogFilter
            {
                FromUtc = date,
                ToUtc = date.AddDays(-1)
            })).StatusCode);
        Assert.Equal(403, (await auditLogs.GetAll(memberId, 1, 20, null, default)).StatusCode);
    }

    [Fact]
    public async Task UserListsAndDetailsResolveRestaurantNamesInOneBatch()
    {
        var restaurant = Guid.NewGuid();
        (await db.Users.FindAsync(memberId))!.RestaurantId = restaurant;
        await db.SaveChangesAsync();
        var names = new QueryRestaurantNames(restaurant);
        var service = new UserService(new UnitOfWork(db),
            Options.Create(new UserManagementSettings
            {
                AdminRoleIds = ["admin"]
            }), restaurantNames: names);
        var result = await service.GetAll(adminId, 1, 20, null, default);
        var page = Assert.IsType<PagedResult<UserDTO>>(result.Result);
        Assert.Equal("Seoul", page.Items.Single(user => user.UserId == memberId).RestaurantName);
        Assert.Null(page.Items.Single(user => user.UserId == adminId).RestaurantName);
        Assert.Equal(1, names.Calls);
        var detail = Assert.IsType<UserDTO>((await service.GetById(adminId, memberId, default)).Result);
        Assert.Equal("Seoul", detail.RestaurantName);
        Assert.Equal(2, names.Calls);
    }

    private sealed class QueryRestaurantNames(Guid restaurant) : IRestaurantNames
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyDictionary<Guid, string>> GetAsync(Guid[] ids, CancellationToken ct)
        {
            Calls++;
            Assert.Equal(restaurant, Assert.Single(ids));
            return Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string> { [restaurant] = "Seoul" });
        }
    }
}
