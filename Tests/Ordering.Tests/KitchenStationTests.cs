using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ordering.BLL.Implements;
using Ordering.BLL.Interfaces;
using Ordering.Common.DTOs;
using Ordering.Common.Exceptions;
using Ordering.DAL.Implements;
using Ordering.DAL.Models;
using Shared.Persistence.UnitOfWorks;
using Xunit;

namespace Ordering.Tests;

public sealed class KitchenStationTests
{
    [Fact]
    public async Task CreateNormalizeUpdateFilterAndDisable()
    {
        using var fixture = new Fixture();
        var station = await fixture.Service.Create(new KitchenStationWriteDTO
        {
            KitchenStationCode = " hot ",
            KitchenStationName = " Hot kitchen "
        }, default);
        Assert.Equal("HOT", station.KitchenStationCode);
        Assert.True(station.IsActive);
        await fixture.Service.Update(station.KitchenStationId, new KitchenStationWriteDTO
        {
            KitchenStationCode = "HOT",
            KitchenStationName = "Renamed"
        }, default);
        await fixture.Service.SetStatus(station.KitchenStationId, false, default);
        var page = await fixture.Service.GetAll(1, 20, "HOT", false, default);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal("Renamed", Assert.Single(page.Items).KitchenStationName);
        Assert.Empty((await fixture.Service.GetAll(1, 20, null, true, default)).Items);
        Assert.False(await new KitchenTaskRepository(fixture.Db).IsStationActiveAsync(station.KitchenStationId, default));
        Assert.Equal(station.KitchenStationId, (await fixture.Service.GetById(station.KitchenStationId, default)).KitchenStationId);
    }

    [Fact]
    public async Task DuplicateCodeRejectedWithoutInserting()
    {
        using var fixture = new Fixture();
        var request = new KitchenStationWriteDTO
        {
            KitchenStationCode = "HOT",
            KitchenStationName = "Hot"
        };
        await fixture.Service.Create(request, default);
        request.KitchenStationCode = " hot ";
        var error = await Assert.ThrowsAsync<OrderingException>(() => fixture.Service.Create(request, default));
        Assert.Equal(409, error.StatusCode);
        Assert.Equal(1, await fixture.Db.KitchenStations.CountAsync());
    }

    [Fact]
    public async Task UnauthorizedWritesAndReadsAreRejectedBeforeDatabaseAccess()
    {
        using var fixture = new Fixture();
        fixture.Access.Denied = true;
        var error = await Assert.ThrowsAsync<OrderingException>(() => fixture.Service.Create(new(), default));
        Assert.Equal(403, error.StatusCode);
        await Assert.ThrowsAsync<OrderingException>(() => fixture.Service.GetAll(1, 20, null, null, default));
        await Assert.ThrowsAsync<OrderingException>(() => fixture.Service.GetById(Guid.NewGuid(), default));
        await Assert.ThrowsAsync<OrderingException>(() => fixture.Service.Update(Guid.NewGuid(), new(), default));
        await Assert.ThrowsAsync<OrderingException>(() => fixture.Service.SetStatus(Guid.NewGuid(), false, default));
        Assert.Empty(await fixture.Db.KitchenStations.ToListAsync());
    }

    [Fact]
    public async Task InvalidFieldsPaginationAndMissingStationHaveExplicitErrors()
    {
        using var fixture = new Fixture();
        Assert.Equal(400, (await Assert.ThrowsAsync<OrderingException>(() =>
            fixture.Service.Create(new KitchenStationWriteDTO
            {
                KitchenStationCode = "bad code",
                KitchenStationName = "Hot"
            }, default))).StatusCode);
        Assert.Equal(400, (await Assert.ThrowsAsync<OrderingException>(() =>
            fixture.Service.GetAll(int.MaxValue, 100, null, null, default))).StatusCode);
        Assert.Equal(404, (await Assert.ThrowsAsync<OrderingException>(() =>
            fixture.Service.GetById(Guid.NewGuid(), default))).StatusCode);
    }

    private sealed class Access : IKitchenStationAccess
    {
        public bool Denied { get; set; }

        public Task RequireAsync(bool write, CancellationToken ct)
        {
            if (Denied)
            {
                throw new OrderingException(403, "Forbidden");
            }
            return Task.CompletedTask;
        }

        public Task<Guid> RequireKitchenTaskAccessAsync(CancellationToken ct)
        {
            if (Denied)
            {
                throw new OrderingException(403, "Forbidden");
            }
            return Task.FromResult(Guid.Parse("10000000-0000-0000-0000-000000000005"));
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public OrderingDbContext Db { get; }
        public Access Access { get; } = new();
        public KitchenStationService Service { get; }

        public Fixture()
        {
            connection.Open();
            Db = new OrderingDbContext(new DbContextOptionsBuilder<OrderingDbContext>()
                .UseSqlite(connection).Options);
            Db.Database.EnsureCreated();
            Service = new KitchenStationService(new KitchenStationRepository(Db),
                new OrderingTransaction(new UnitOfWork<OrderingDbContext>(Db)), Access);
        }

        public void Dispose()
        {
            Db.Dispose();
            connection.Dispose();
        }
    }
}
