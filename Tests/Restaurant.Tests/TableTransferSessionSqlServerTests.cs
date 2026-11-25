using System.Text.Json;
using Backend.Contracts.Restaurant;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Restaurant.DAL.Models;
using Shared.Common.TableTransfers;
using Xunit;

namespace Restaurant.Tests;

public sealed partial class ReservationSqlServerTests
{
    [SqlServerFact]
    public async Task TableTransferSqlConcurrentPrepareFinishAndSplitMergedTable()
    {
        var location = new LocationIds(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            db.Restaurants.Add(new Restaurant.DAL.Models.Restaurant
            {
                RestaurantId = location.RestaurantId,
                RestaurantName = "Transfer test",
                Address = "Test address",
                Hotline = "0901234567",
                TimeZone = "UTC"
            });
            db.Areas.Add(new Area
            {
                AreaId = location.AreaId,
                RestaurantId = location.RestaurantId,
                AreaCode = "DINING",
                AreaName = "Dining",
                ServiceType = "DineIn",
                SpaceType = "Indoor",
                IsActive = true
            });
            db.Tables.Add(new Table
            {
                TableId = location.TableId,
                AreaId = location.AreaId,
                TableCode = "SOURCE",
                Capacity = 4,
                CleaningStatus = "Clean",
                IsActive = true
            });
            db.Set<RestaurantStaff>().Add(new RestaurantStaff
            {
                RestaurantId = location.RestaurantId,
                UserIdRef = _users["waiter"],
                IsActive = true
            });
            await db.SaveChangesAsync();
        }
        var source = await SeedOpenPaymentSession(location);
        var targetTable = Guid.NewGuid();
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            db.Tables.Add(new Table
            {
                TableId = targetTable,
                AreaId = location.AreaId,
                TableCode = "TRANSFER",
                Capacity = 4,
                CleaningStatus = "Clean",
                IsActive = true
            });
            await db.SaveChangesAsync();
        }
        using var channel = GrpcChannel.ForAddress("http://restaurant.test", new GrpcChannelOptions
        {
            HttpHandler = _restaurant.Server.CreateHandler()
        });
        var client = new TableTransferSessionGrpc.TableTransferSessionGrpcClient(channel);
        var headers = new Metadata
        {
            { "authorization", "Bearer " + _tokens["waiter"] },
            { "x-table-transfer-key", "table-transfer-sql-test-key" }
        };
        var split = new TableTransferDTO
        {
            OperationId = Guid.NewGuid(),
            SourceSessionId = source,
            TargetTableId = targetTable,
            Mode = "Split",
            Reason = "Khách tách bàn",
            Items = [new TableTransferLineDTO
            {
                OrderItemId = Guid.NewGuid(),
                Quantity = 1
            }]
        };
        async Task<TableTransferResultDTO> Prepare(TableTransferDTO command)
        {
            var reply = await client.PrepareAsync(new TableTransferSessionRequest
            {
                Payload = JsonSerializer.Serialize(command)
            }, headers);
            return JsonSerializer.Deserialize<TableTransferResultDTO>(reply.Payload)!;
        }
        async Task Finish(Guid id)
        {
            await client.FinishAsync(new TableTransferFinishRequest
            {
                OperationId = id.ToString()
            }, headers);
        }
        var results = await Task.WhenAll(Prepare(split), Prepare(split));
        Assert.Equal(results[0].TargetSessionId, results[1].TargetSessionId);
        await Task.WhenAll(Finish(split.OperationId), Finish(split.OperationId));
        var targetSession = results[0].TargetSessionId;

        var merge = new TableTransferDTO
        {
            OperationId = Guid.NewGuid(),
            SourceSessionId = source,
            TargetTableId = targetTable,
            Mode = "Merge",
            Reason = "Khách gộp lại"
        };
        await Prepare(merge);
        await Task.WhenAll(Finish(merge.OperationId), Finish(merge.OperationId));

        var detach = new TableTransferDTO
        {
            OperationId = Guid.NewGuid(),
            SourceSessionId = targetSession,
            TargetTableId = location.TableId,
            Mode = "Split",
            Reason = "Tách lại bàn trong nhóm",
            Items = split.Items
        };
        var detached = await Prepare(detach);
        await Finish(detach.OperationId);
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            var links = await db.SessionTables.Where(value => value.ReleasedAt == null).ToListAsync();
            Assert.Equal(2, links.Count);
            Assert.Equal(2, links.Select(value => value.TableId).Distinct().Count());
            Assert.Equal(detached.TargetSessionId, links.Single(value => value.TableId == location.TableId).TableSessionId);
            Assert.Equal(targetSession, links.Single(value => value.TableId == targetTable).TableSessionId);
            Assert.Equal("Closed", (await db.TableSessions.FindAsync(source))!.Status);
        }
    }
}
