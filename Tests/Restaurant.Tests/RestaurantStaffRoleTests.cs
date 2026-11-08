using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Restaurant.BLL.Implements;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;
using Restaurant.Common.Reservations;
using Restaurant.Common.Settings;
using Restaurant.DAL.Interfaces;
using Restaurant.DAL.Models;
using Xunit;

namespace Restaurant.Tests;

public sealed partial class GatewayFlowTests
{
    [Fact]
    public async Task AdministratorCanAssignManagerWithoutGrantingReservationPermissions()
    {
        var restaurant = await CreateRestaurant();
        var area = await CreateArea(restaurant.RestaurantId, "MANAGER");
        var table = await CreateTable(area.AreaId);
        var pending = await SeedReservation(ReservationStatuses.Pending, _reservationClock.Now.AddHours(1), null, table.TableId);
        var managerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        using (var scope = _restaurant.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            var settings = services.GetRequiredService<IOptions<RestaurantStaffSettings>>();
            Assert.Contains("manager", settings.Value.AssignableRoleIds);
            Assert.DoesNotContain("manager", services.GetRequiredService<IOptions<ReservationSettings>>().Value.StaffRoleIds);
            var service = new RestaurantStaffService(new AssignmentAdministratorAccess(),
                new AssignmentManagerIdentity(managerId), services.GetRequiredService<IRestaurantStaffRepository>(),
                services.GetRequiredService<IRestaurantRepository>(), services.GetRequiredService<IReservationTransaction>(), settings);
            var assignment = await service.Create(restaurant.RestaurantId, managerId, CancellationToken.None);
            Assert.True(assignment.IsActive);
            Assert.Equal(managerId, assignment.UserIdRef);
            Assert.Equal(restaurant.RestaurantId, assignment.RestaurantId);
            Assert.Single(await services.GetRequiredService<RestaurantDbContext>().RestaurantStaffs.ToListAsync());
        }

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "manager");
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.PostAsJsonAsync(
            $"/api/restaurants/{restaurant.RestaurantId}/staff", new
            {
                userIdRef = Guid.NewGuid()
            })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.GetAsync(
            $"/api/restaurants/{restaurant.RestaurantId}/staff")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.DeleteAsync(
            $"/api/restaurants/{restaurant.RestaurantId}/staff/{managerId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.PostAsJsonAsync(
            $"/api/reservations/{pending}/confirm", new
            {
                tableIds = new[] { table.TableId }
            })).StatusCode);
        using var finalScope = _restaurant.Services.CreateScope();
        var db = finalScope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        Assert.Equal(ReservationStatuses.Pending, (await db.Reservations.SingleAsync()).Status);
        Assert.True((await db.RestaurantStaffs.SingleAsync()).IsActive);
    }

    private sealed class AssignmentAdministratorAccess : IManagementAccess
    {
        public Task<Guid> RequireAdmin(CancellationToken ct)
        {
            return Task.FromResult(Guid.NewGuid());
        }
    }

    private sealed class AssignmentManagerIdentity(Guid userId) : IStaffIdentity
    {
        public Task<StaffActorDto> GetCurrentAsync(CancellationToken ct)
        {
            throw new InvalidOperationException("Assignment creation must authenticate through administrator access.");
        }

        public Task<StaffActorDto> GetUserAsync(Guid requestedUserId, CancellationToken ct)
        {
            Assert.Equal(userId, requestedUserId);
            return Task.FromResult(new StaffActorDto
            {
                UserId = userId,
                RoleId = "manager"
            });
        }
    }
}
