using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ordering.BLL.Implements;
using Ordering.BLL.Interfaces;
using Ordering.Common.DTOs;
using Ordering.Common.Exceptions;
using Ordering.Common.Settings;
using Ordering.DAL.Implements;
using Ordering.DAL.Models;
using Shared.Persistence.UnitOfWorks;
using Xunit;

namespace Ordering.Tests;

public sealed class OrderingFlowTests
{
    [Fact]
    public async Task GuestSubmissionIsAtomicAndReplayDoesNotRevalidateOrDuplicate()
    {
        using var app = new Fixture();
        var cart = await app.FilledCart();
        var request = new CreateOrderDTO
        {
            CartId = cart.CartId,
            CartVersion = cart.Version
        };
        var first = await app.Orders.Create(app.Session, "submit-1", request, "guest", default);
        Assert.True(first.Created);
        Assert.Equal("PendingConfirmation", first.Order.Status);
        Assert.Equal(120000m, first.Order.TotalAmount);
        Assert.Empty(await app.Db.KitchenTasks.ToListAsync());
        Assert.Empty(await app.Db.CartItems.ToListAsync());
        Assert.Empty(await app.Db.CartItemOptions.ToListAsync());
        app.Valid = false;
        var replay = await app.Orders.Create(app.Session, "submit-1", request, "guest", default);
        Assert.False(replay.Created);
        Assert.Equal(first.Order.OrderId, replay.Order.OrderId);
        Assert.Single(await app.Db.Orders.ToListAsync());
        request.CartVersion = "different";
        await Error(409, () => app.Orders.Create(app.Session, "submit-1", request, "guest", default));
        request.CartVersion = cart.Version;
        await Error(409, () => app.Orders.Create(app.Session, "submit-2", request, "guest", default));
    }

    [Fact]
    public async Task ConfirmationPreservesPricesAndEnqueuesExactlyOnce()
    {
        using var app = new Fixture();
        var cart = await app.FilledCart();
        var created = await app.Orders.Create(app.Session, "submit", new CreateOrderDTO
        {
            CartId = cart.CartId,
            CartVersion = cart.Version
        }, "guest", default);
        await Error(403, () => app.Orders.Confirm(app.Session, created.Order.OrderId, created.Order.Version, default));
        app.Staff = true;
        await Error(409, () => app.Orders.Confirm(app.Session, created.Order.OrderId,
            Convert.ToBase64String(new byte[8]), default));
        app.Price = 99000;
        var confirmed = await app.Orders.Confirm(app.Session, created.Order.OrderId, created.Order.Version, default);
        Assert.Equal("Confirmed", confirmed.Status);
        Assert.Equal(120000m, confirmed.TotalAmount);
        Assert.Single(await app.Db.KitchenTasks.ToListAsync());
        Assert.Single(await app.Db.Events.ToListAsync());
        await app.Orders.Confirm(app.Session, confirmed.OrderId, created.Order.Version, default);
        Assert.Single(await app.Db.KitchenTasks.ToListAsync());
    }

    [Fact]
    public async Task ServeItemRequiresStaffAndReadyStatus()
    {
        using var app = new Fixture();
        var cart = await app.FilledCart();
        var created = await app.Orders.Create(app.Session, "submit-serve", new CreateOrderDTO
        {
            CartId = cart.CartId,
            CartVersion = cart.Version
        }, "guest", default);
        app.Staff = true;
        var confirmed = await app.Orders.Confirm(app.Session, created.Order.OrderId, created.Order.Version, default);
        var orderItem = Assert.Single(confirmed.Items);

        await Error(400, () => app.Orders.ServeItem(app.Session, confirmed.OrderId, orderItem.OrderItemId, null, default));

        var dbItem = await app.Db.OrderItems.FirstAsync(i => i.OrderItemId == orderItem.OrderItemId);
        dbItem.Status = "Ready";
        await app.Db.SaveChangesAsync();

        app.Staff = false;
        await Error(403, () => app.Orders.ServeItem(app.Session, confirmed.OrderId, orderItem.OrderItemId, null, default));

        app.Staff = true;
        var served = await app.Orders.ServeItem(app.Session, confirmed.OrderId, orderItem.OrderItemId, null, default);
        Assert.Equal("Served", served.Status);
        Assert.Equal("Served", Assert.Single(served.Items).Status);
    }

    [Fact]
    public async Task CartOwnershipVersionAndReplacementSurviveReload()
    {
        using var app = new Fixture();
        var cart = await app.FilledCart();
        app.Db.ChangeTracker.Clear();
        var loaded = await app.Carts.GetById(app.Session, cart.CartId, "guest", default);
        Assert.Equal(cart.Version, loaded.Version);
        await Error(409, () => app.Carts.Update(app.Session, cart.CartId,
            new CartWriteDTO { Version = "stale" }, "guest", default));
        var oldGuest = app.Guest;
        app.Guest = Guid.NewGuid();
        await Error(404, () => app.Carts.GetById(app.Session, cart.CartId, "guest", default));
        app.Guest = oldGuest;
        var empty = await app.Carts.Update(app.Session, cart.CartId,
            new CartWriteDTO { Version = cart.Version }, "guest", default);
        Assert.Empty(empty.Items);
        Assert.Empty(await app.Db.CartItemOptions.ToListAsync());
        await app.Carts.Delete(app.Session, cart.CartId, empty.Version, "guest", default);
        Assert.Empty(await app.Db.Carts.ToListAsync());
    }

    [Fact]
    public async Task UnavailableItemsLeaveCartIntactAndDoNotCreateOrders()
    {
        using var app = new Fixture();
        var cart = await app.FilledCart();
        app.Valid = false;
        await Error(409, () => app.Orders.Create(app.Session, "submit", new CreateOrderDTO
        {
            CartId = cart.CartId,
            CartVersion = cart.Version
        }, "guest", default));
        Assert.Empty(await app.Db.Orders.ToListAsync());
        Assert.Single(await app.Db.CartItems.ToListAsync());
    }

    [Fact]
    public async Task StaffOrderUsesStationAndGuestInSameSessionCanReadIt()
    {
        using var app = new Fixture();
        app.Staff = true;
        var created = await app.Orders.Create(app.Session, "staff", new CreateOrderDTO
        {
            Items = app.Lines()
        }, "", default);
        Assert.Equal("Confirmed", created.Order.Status);
        Assert.Equal(app.Station, (await app.Db.KitchenTasks.SingleAsync()).KitchenStationId);
        Assert.Equal(app.User, created.Order.ConfirmedBy);
        app.Staff = false;
        var guestOrder = await app.Orders.GetById(app.Session, created.Order.OrderId, "guest", default);
        Assert.Equal(created.Order.OrderId, guestOrder.OrderId);
        Assert.Single(await app.Orders.GetAll(app.Session, 1, 20, "guest", default));
        app.Closed = true;
        await Error(409, () => app.Carts.Create(app.Session, "guest", default));
    }

    [Fact]
    public async Task AmbiguousKitchenFailsWithoutPartialOrderAndExplicitStationResolvesIt()
    {
        using var app = new Fixture();
        app.Staff = true;
        app.Db.KitchenStations.Add(new KitchenStation
        {
            KitchenStationId = Guid.NewGuid(),
            KitchenStationCode = "BAR",
            KitchenStationName = "Bar",
            IsActive = true
        });
        await app.Db.SaveChangesAsync();
        var request = new CreateOrderDTO { Items = app.Lines() };
        await Error(409, () => app.Orders.Create(app.Session, "station", request, "", default));
        Assert.Empty(await app.Db.Orders.ToListAsync());
        app.ItemStation = app.Station;
        await app.Orders.Create(app.Session, "station", request, "", default);
        Assert.Equal(app.Station, (await app.Db.KitchenTasks.SingleAsync()).KitchenStationId);
    }

    private static async Task Error(int status, Func<Task> action)
    {
        var error = await Assert.ThrowsAsync<OrderingException>(action);
        Assert.Equal(status, error.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TwoItemsRouteToDifferentStationsAndDisabledStationRollsBack(bool staff)
    {
        using var app = new Fixture();
        var drink = Guid.NewGuid();
        var bar = new KitchenStation
        {
            KitchenStationId = Guid.NewGuid(),
            KitchenStationCode = "BAR",
            KitchenStationName = "Bar",
            IsActive = false
        };
        app.Db.KitchenStations.Add(bar);
        await app.Db.SaveChangesAsync();
        app.Routes[app.Item] = app.Station;
        app.Routes[drink] = bar.KitchenStationId;
        var lines = app.Lines();
        lines.Add(new SelectionDTO
        {
            ItemId = drink,
            Quantity = 1
        });
        OrderDTO? pending = null;
        if (!staff)
        {
            var cart = await app.Carts.Create(app.Session, "guest", default);
            cart = await app.Carts.Update(app.Session, cart.CartId, new CartWriteDTO
            {
                Version = cart.Version,
                Items = lines
            }, "guest", default);
            pending = (await app.Orders.Create(app.Session, "guest-routing", new CreateOrderDTO
            {
                CartId = cart.CartId,
                CartVersion = cart.Version
            }, "guest", default)).Order;
        }
        app.Staff = true;
        var request = new CreateOrderDTO
        {
            Items = lines
        };
        if (staff)
        {
            await Error(409, () => app.Orders.Create(app.Session, "routing", request, "", default));
            Assert.Empty(await app.Db.Orders.ToListAsync());
        }
        else
        {
            await Error(409, () => app.Orders.Confirm(app.Session, pending!.OrderId,
                pending.Version, default));
            Assert.Equal("PendingConfirmation", (await app.Db.Orders.SingleAsync()).Status);
        }
        Assert.Empty(await app.Db.KitchenTasks.ToListAsync());
        bar.IsActive = true;
        await app.Db.SaveChangesAsync();
        if (staff)
        {
            await app.Orders.Create(app.Session, "routing", request, "", default);
        }
        else
        {
            await app.Orders.Confirm(app.Session, pending!.OrderId,
                pending.Version, default);
        }
        var tasks = await app.Db.KitchenTasks.Include(task => task.OrderItem).ToListAsync();
        Assert.Equal(2, tasks.Count);
        Assert.Equal(app.Station, tasks.Single(task => task.OrderItem.ItemRef == app.Item).KitchenStationId);
        Assert.Equal(bar.KitchenStationId, tasks.Single(task => task.OrderItem.ItemRef == drink).KitchenStationId);
    }

    [Fact]
    public async Task CourseSequencingAssignsProperInitialStatusAndPriorityBasedOnChannel()
    {
        using var app = new Fixture();
        var dessert = Guid.NewGuid();
        var appetizer = Guid.NewGuid();
        var slowCook = Guid.NewGuid();

        app.Names[dessert] = "Chè khúc bạch";
        app.Names[appetizer] = "Gỏi ngó sen tôm thịt";
        app.Names[slowCook] = "Bò hầm tiêu xanh";

        app.Routes[dessert] = app.Station;
        app.Routes[appetizer] = app.Station;
        app.Routes[slowCook] = app.Station;

        app.Staff = true;

        // 1. DineIn Order
        var dineInOrder = await app.Orders.Create(app.Session, "dinein-seq", new CreateOrderDTO
        {
            Channel = "DineIn",
            Items =
            [
                new SelectionDTO { ItemId = dessert, Quantity = 1 },
                new SelectionDTO { ItemId = appetizer, Quantity = 1 },
                new SelectionDTO { ItemId = slowCook, Quantity = 1 }
            ]
        }, "", default);

        var tasks = await app.Db.KitchenTasks.Include(t => t.OrderItem).ToListAsync();
        var dessertTask = tasks.Single(t => t.OrderItem.ItemRef == dessert);
        var appTask = tasks.Single(t => t.OrderItem.ItemRef == appetizer);
        var slowTask = tasks.Single(t => t.OrderItem.ItemRef == slowCook);

        // DineIn: Dessert is placed on Hold (Priority 20)
        Assert.Equal("Hold", dessertTask.Status);
        Assert.Equal(20, dessertTask.Priority);

        // Appetizer: Pending, Priority 100
        Assert.Equal("Pending", appTask.Status);
        Assert.Equal(100, appTask.Priority);

        // Slow-cook: Pending, Priority 70
        Assert.Equal("Pending", slowTask.Status);
        Assert.Equal(70, slowTask.Priority);

        // 2. Takeaway Order
        var takeawayDessert = Guid.NewGuid();
        app.Names[takeawayDessert] = "Chè đậu đỏ";
        app.Routes[takeawayDessert] = app.Station;

        var takeawayOrder = await app.Orders.Create(app.Session, "takeaway-seq", new CreateOrderDTO
        {
            Channel = "Takeaway",
            Items =
            [
                new SelectionDTO { ItemId = takeawayDessert, Quantity = 1 }
            ]
        }, "", default);

        var takeawayTask = await app.Db.KitchenTasks.Include(t => t.OrderItem)
            .SingleAsync(t => t.OrderItem.ItemRef == takeawayDessert);

        // Takeaway: No course delay/Hold, immediate cooking queue
        Assert.Equal("Pending", takeawayTask.Status);
        Assert.Equal(50, takeawayTask.Priority);
    }

    private sealed class Fixture : IOrderingAccess, ICatalogOrderClient, IDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public TestDb Db { get; }
        public CartService Carts { get; }
        public OrderService Orders { get; }
        public Guid Session { get; } = Guid.NewGuid();
        public Guid Guest { get; set; } = Guid.NewGuid();
        public Guid User { get; } = Guid.NewGuid();
        public Guid Table { get; } = Guid.NewGuid();
        public Guid Item { get; } = Guid.NewGuid();
        public Guid Option { get; } = Guid.NewGuid();
        public Guid Recipe { get; } = Guid.NewGuid();
        public Guid Station { get; } = Guid.NewGuid();
        public Guid? ItemStation { get; set; }
        public Dictionary<Guid, Guid> Routes { get; } = new();
        public Dictionary<Guid, string> Names { get; } = new();
        public bool Staff { get; set; }
        public bool Closed { get; set; }
        public bool Valid { get; set; } = true;
        public decimal Price { get; set; } = 50000;

        public Fixture()
        {
            connection.Open();
            Db = new TestDb(new DbContextOptionsBuilder<OrderingDbContext>().UseSqlite(connection).Options);
            Db.Database.EnsureCreated();
            Db.KitchenStations.Add(new KitchenStation
            {
                KitchenStationId = Station,
                KitchenStationCode = "HOT",
                KitchenStationName = "Hot kitchen",
                IsActive = true
            });
            Db.SaveChanges();
            var carts = new CartRepository(Db);
            var transaction = new OrderingTransaction(new UnitOfWork<OrderingDbContext>(Db));
            Carts = new CartService(carts, transaction, this, this, TimeProvider.System);
            Orders = new OrderService(new OrderRepository(Db), carts, new KitchenTaskRepository(Db),
                transaction, this, this, Options.Create(new OrderingSettings()), TimeProvider.System);
        }

        public List<SelectionDTO> Lines()
        {
            return [new SelectionDTO
            {
                ItemId = Item,
                Quantity = 2,
                Options = [new OptionSelectionDTO
                {
                    OptionId = Option,
                    Quantity = 2
                }]
            }];
        }

        public async Task<CartDTO> FilledCart()
        {
            var cart = await Carts.Create(Session, "guest", default);
            return await Carts.Update(Session, cart.CartId, new CartWriteDTO
            {
                Version = cart.Version,
                Items = Lines()
            }, "guest", default);
        }

        public Task<OrderingActor> RequireAsync(Guid sessionId, Guid? tableId, string guestToken, CancellationToken ct)
        {
            if (Closed)
            {
                throw new OrderingException(409, "Session closed");
            }

            return Task.FromResult(new OrderingActor(sessionId, tableId ?? Table, Staff ? User : null,
                Staff ? null : Guest, DateTimeOffset.UtcNow.AddHours(1)));
        }

        public Task<QuoteDTO> ValidateAsync(OrderingActor actor, string guestToken, List<SelectionDTO> items, CancellationToken ct)
        {
            return Task.FromResult(new QuoteDTO
            {
                IsValid = Valid,
                Message = Valid ? "OK" : "Recipe unavailable",
                EvaluatedAt = DateTimeOffset.UtcNow,
                Items = items.Select(item => new QuotedItemDTO
                {
                    ItemId = item.ItemId,
                    KitchenStationId = Routes.TryGetValue(item.ItemId, out var route) ? route : ItemStation,
                    Name = Names.TryGetValue(item.ItemId, out var name) ? name : "Pho",
                    RecipeVersionId = Recipe,
                    Quantity = item.Quantity,
                    UnitPrice = Price,
                    Options = item.Options.Select(option => new QuotedOptionDTO
                    {
                        OptionId = option.OptionId,
                        Name = "Extra beef",
                        Quantity = option.Quantity,
                        ExtraPrice = 5000
                    }).ToList()
                }).ToList()
            });
        }

        public void Dispose()
        {
            Db.Dispose();
            connection.Dispose();
        }
    }

    private sealed class TestDb(DbContextOptions<OrderingDbContext> options) : OrderingDbContext(options)
    {
        private long version;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Order>().Property(entity => entity.Version).ValueGeneratedNever();
            modelBuilder.Entity<OrderItem>().Property(entity => entity.Version).ValueGeneratedNever();
            modelBuilder.Entity<KitchenTask>().Property(entity => entity.Version).ValueGeneratedNever();
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            foreach (var entry in ChangeTracker.Entries().Where(entry => entry.State is EntityState.Added or EntityState.Modified))
            {
                if (entry.Entity is Order or OrderItem or KitchenTask)
                {
                    entry.Property("Version").CurrentValue = BitConverter.GetBytes(++version);
                }
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
