using Identity.BLL.Implements;
using Identity.BLL.Interfaces;
using Identity.DAL.Implements;
using Identity.DAL.UnitOfWorks;
using Identity.Domain.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shared.Common.Notifications;
using Xunit;

namespace Identity.Tests;

public sealed class NotificationTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly IdentityDbContext db;
    private readonly NotificationService notifications;
    private readonly AnnouncementService announcements;
    private readonly NotificationSettingService settings;
    private readonly Audience audience = new();
    private readonly Guid restaurant = Guid.NewGuid();
    private readonly Guid otherRestaurant = Guid.NewGuid();
    private readonly Guid admin = Guid.NewGuid();
    private readonly Guid waiter = Guid.NewGuid();
    private readonly Guid secondWaiter = Guid.NewGuid();
    private readonly Guid cashier = Guid.NewGuid();
    private readonly Guid customer = Guid.NewGuid();
    private readonly Guid otherCustomer = Guid.NewGuid();
    private readonly Guid foreignWaiter = Guid.NewGuid();
    private readonly Guid kitchen = Guid.NewGuid();

    public NotificationTests()
    {
        connection.Open();
        db = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        foreach (var role in new[] { "admin", "waiter", "cashier", "customer", "kitchen" })
        {
            db.Roles.Add(new Role
            {
                RoleId = role,
                RoleName = role
            });
        }
        AddUser(admin, "admin", restaurant);
        AddUser(waiter, "waiter", restaurant);
        AddUser(secondWaiter, "waiter", restaurant);
        AddUser(cashier, "cashier", restaurant);
        AddUser(kitchen, "kitchen", restaurant);
        AddUser(customer, "customer", null);
        AddUser(otherCustomer, "customer", null);
        AddUser(foreignWaiter, "waiter", otherRestaurant);
        db.SaveChanges();
        db.ChangeTracker.Clear();
        var access = new NotificationAccess(new UserRepository(db), audience);
        var work = new UnitOfWork(db);
        var repository = new NotificationRepository(db);
        var settingRepository = new NotificationSettingRepository(db);
        notifications = new NotificationService(repository, access, work, audience);
        announcements = new AnnouncementService(new AnnouncementRepository(db), repository, settingRepository, access, work, audience);
        settings = new NotificationSettingService(settingRepository, access, work);
    }

    private void AddUser(Guid id, string role, Guid? restaurantId)
    {
        db.Users.Add(new User
        {
            UserId = id,
            RoleId = role,
            RestaurantId = restaurantId,
            IsActive = true,
            UserName = id.ToString(),
            FullName = role,
            Email = id + "@example.test",
            PasswordHash = "test-only",
            CreatedAt = DateTime.UtcNow
        });
        if (restaurantId.HasValue)
        {
            audience.Assignments.Add(id, restaurantId.Value);
        }
    }

    private NotificationEvent Event(string type = "TakeawayReady")
    {
        return new NotificationEvent
        {
            EventKey = "Ordering:" + type + ":" + Guid.NewGuid(),
            RestaurantId = restaurant,
            EntityId = Guid.NewGuid(),
            CustomerId = customer,
            Type = type,
            OccurredAt = DateTime.UtcNow,
            KitchenUserId = kitchen
        };
    }

    private static AnnouncementWrite Draft(Guid? version = null)
    {
        return new AnnouncementWrite
        {
            Title = "Shift meeting",
            Message = "Please attend the shift meeting.",
            RecipientRoleIds = ["waiter"],
            Version = version
        };
    }

    [Fact]
    public async Task EventRetryDoesNotDuplicateAndReadersAreIndependent()
    {
        var value = Event();
        await notifications.Publish(value, default);
        await notifications.Publish(value, default);
        Assert.Single(await db.Notifications.ToListAsync());
        Assert.Equal(3, await db.NotificationRecipients.CountAsync());
        var mine = await notifications.GetAll(customer, null, new NotificationQuery(), default);
        var item = Assert.Single(mine.Items);
        Assert.Empty((await notifications.GetById(customer, null, item.Id, default)).Recipients);
        Assert.Equal(1, await notifications.UnreadCount(customer, default));
        await notifications.MarkRead(customer, item.Id, default);
        await notifications.MarkRead(customer, item.Id, default);
        Assert.Equal(0, await notifications.UnreadCount(customer, default));
        Assert.Equal(1, await notifications.UnreadCount(waiter, default));
        await notifications.MarkRead(waiter, null, default);
        Assert.Equal(0, await notifications.UnreadCount(waiter, default));
        Assert.Equal(1, await notifications.UnreadCount(secondWaiter, default));
    }

    [Fact]
    public async Task PersonalAndRestaurantHistoryRejectForeignAccessAndRemovedStaff()
    {
        await notifications.Publish(Event(), default);
        var id = await db.Notifications.Select(value => value.NotificationId).SingleAsync();
        await Error(404, () => notifications.GetById(otherCustomer, null, id, default));
        await Error(404, () => notifications.GetById(foreignWaiter, null, id, default));
        await Error(404, () => notifications.MarkRead(otherCustomer, id, default));
        await Error(403, () => notifications.GetAll(admin, otherRestaurant, new NotificationQuery(), default));
        await Error(403, () => notifications.GetAll(waiter, restaurant, new NotificationQuery(), default));
        var history = await notifications.GetById(admin, restaurant, id, default);
        Assert.Equal(3, history.Recipients.Count);
        Assert.Equal(1, await notifications.UnreadCount(customer, default));
        audience.Assignments.Remove(waiter);
        Assert.Empty((await notifications.GetAll(waiter, null, new NotificationQuery(), default)).Items);
        await Error(404, () => notifications.GetById(waiter, null, id, default));
    }

    [Fact]
    public async Task AnnouncementDraftSendRetryAndVersionGuards()
    {
        var draft = await announcements.Create(admin, restaurant, Draft(), default);
        Assert.Empty(await db.Notifications.ToListAsync());
        await Error(409, () => announcements.Update(admin, restaurant, draft.Id, Draft(Guid.NewGuid()), default));
        var updated = await announcements.Update(admin, restaurant, draft.Id, Draft(draft.Version), default);
        await Error(409, () => announcements.Send(admin, restaurant, draft.Id, draft.Version, default));
        audience.Assignments.Remove(secondWaiter);
        var sent = await announcements.Send(admin, restaurant, draft.Id, updated.Version, default);
        var retry = await announcements.Send(admin, restaurant, draft.Id, updated.Version, default);
        Assert.Equal(sent.NotificationId, retry.NotificationId);
        Assert.Single(await db.Notifications.ToListAsync());
        Assert.Equal(waiter, (await db.NotificationRecipients.SingleAsync()).UserId);
        await Error(409, () => announcements.Update(admin, restaurant, draft.Id, Draft(sent.Version), default));
        await Error(409, () => announcements.Delete(admin, restaurant, draft.Id, sent.Version, default));
        await Error(403, () => announcements.Create(admin, otherRestaurant, Draft(), default));
        await Error(403, () => announcements.Send(waiter, restaurant, draft.Id, updated.Version, default));
    }

    [Fact]
    public async Task OptionalSettingsBlockSendButNeverMandatoryEvents()
    {
        await Error(409, () => settings.Update(admin, restaurant, "TakeawayReady", false, default));
        await Error(400, () => settings.Update(admin, restaurant, "Unknown", false, default));
        await settings.Update(admin, restaurant, "Announcement", false, default);
        var draft = await announcements.Create(admin, restaurant, Draft(), default);
        await Error(409, () => announcements.Send(admin, restaurant, draft.Id, draft.Version, default));
        await notifications.Publish(Event(), default);
        Assert.Equal(1, await notifications.UnreadCount(customer, default));
        await settings.Update(admin, restaurant, "Announcement", true, default);
        await announcements.Send(admin, restaurant, draft.Id, draft.Version, default);
        Assert.Equal(2, await db.Notifications.CountAsync());
    }

    [Fact]
    public async Task FiltersAndValidationAndDraftDeletion()
    {
        var value = Event();
        await notifications.Publish(value, default);
        await notifications.Publish(Event("TakeawayAccepted"), default);
        var page = await notifications.GetAll(customer, null, new NotificationQuery
        {
            Type = "TakeawayReady",
            EntityId = value.EntityId,
            From = DateTimeOffset.UtcNow.AddMinutes(-1),
            To = DateTimeOffset.UtcNow.AddMinutes(1)
        }, default);
        Assert.Single(page.Items);
        await Error(400, () => notifications.GetAll(customer, null, new NotificationQuery { Page = 0 }, default));
        await Error(400, () => notifications.Publish(Event("Unknown"), default));
        var bad = Draft();
        bad.RecipientRoleIds = ["platform_admin"];
        await Error(400, () => announcements.Create(admin, restaurant, bad, default));
        var draft = await announcements.Create(admin, restaurant, Draft(), default);
        await announcements.Delete(admin, restaurant, draft.Id, draft.Version, default);
        await Error(404, () => announcements.GetById(admin, restaurant, draft.Id, default));
    }

    [Fact]
    public async Task KitchenNotificationOnlyReachesAssignedCookAndRefundContainsNoBankData()
    {
        await notifications.Publish(Event("KitchenOrderAccepted"), default);
        Assert.Equal(kitchen, (await db.NotificationRecipients.SingleAsync()).UserId);
        await notifications.Publish(Event("RefundAccountSubmitted"), default);
        var list = await notifications.GetAll(cashier, null, new NotificationQuery(), default);
        Assert.Equal("RefundAccountSubmitted", Assert.Single(list.Items).Type);
        Assert.DoesNotContain("accountNumber", System.Text.Json.JsonSerializer.Serialize(list));
    }

    private static async Task Error(int status, Func<Task> action)
    {
        Assert.Equal(status, (await Assert.ThrowsAsync<NotificationException>(action)).Status);
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }

    private sealed class Audience : INotificationAudience, INotificationRestaurantAccess
    {
        public Dictionary<Guid, Guid> Assignments { get; } = [];

        public Task<IReadOnlySet<Guid>> GetStaffAsync(Guid restaurantId, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlySet<Guid>>(Assignments.Where(value => value.Value == restaurantId).Select(value => value.Key).ToHashSet());
        }

        public Task<bool> HasAssignmentAsync(Guid userId, Guid restaurantId, CancellationToken ct)
        {
            return Task.FromResult(Assignments.GetValueOrDefault(userId) == restaurantId);
        }
    }
}
