using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend.Contracts.Restaurant;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Restaurant.Common.DTOs;
using Restaurant.DAL.Models;
using Shared.Common.TableTransfers;
using Xunit;

namespace Restaurant.Tests;

public sealed partial class GatewayFlowTests
{
    [Fact]
    public async Task TransferMergeClosesSourceMovesTablesAndRevokesGuestAccess()
    {
        var (request, target) = await TransferSetup(true);
        using var channel = TransferChannel();
        var client = new TableTransferSessionGrpc.TableTransferSessionGrpcClient(channel);
        var first = TransferRead(await client.PrepareAsync(TransferInput(request), TransferHeaders()));
        var replay = TransferRead(await client.PrepareAsync(TransferInput(request), TransferHeaders()));
        Assert.Equal(first.TargetSessionId, replay.TargetSessionId);
        Assert.Equal(target, first.TargetSessionId);
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            Assert.All(await db.TableSessions.ToListAsync(), value => Assert.Equal("Rearranging", value.Status));
            db.GuestAccesses.Add(new GuestAccess
            {
                GuestAccessId = Guid.NewGuid(),
                TableSessionId = request.SourceSessionId,
                TokenHash = new string('a', 64),
                CreatedAt = DateTime.UtcNow,
                ExpiredAt = DateTime.UtcNow.AddHours(1)
            });
            await db.SaveChangesAsync();
        }
        var result = TransferRead(await client.FinishAsync(new TableTransferFinishRequest
        {
            OperationId = request.OperationId.ToString()
        }, TransferHeaders(true)));
        Assert.Equal("Completed", result.Status);
        await client.FinishAsync(new TableTransferFinishRequest { OperationId = request.OperationId.ToString() }, TransferHeaders(true));
        using (var scope = _restaurant.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            Assert.Equal("Closed", (await db.TableSessions.FindAsync(request.SourceSessionId))!.Status);
            Assert.Equal("Open", (await db.TableSessions.FindAsync(target))!.Status);
            Assert.Equal(2, await db.SessionTables.CountAsync(value => value.TableSessionId == target && value.ReleasedAt == null));
            Assert.NotNull((await db.GuestAccesses.SingleAsync()).RevokedAt);
        }
    }

    [Fact]
    public async Task SplitAbortReleasesNewSessionAndRestoresSource()
    {
        var (request, _) = await TransferSetup(false);
        using var channel = TransferChannel();
        var client = new TableTransferSessionGrpc.TableTransferSessionGrpcClient(channel);
        var prepared = TransferRead(await client.PrepareAsync(TransferInput(request), TransferHeaders()));
        await client.FinishAsync(new TableTransferFinishRequest
        {
            OperationId = request.OperationId.ToString(),
            Abort = true
        }, TransferHeaders(true));
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        Assert.Equal("Open", (await db.TableSessions.FindAsync(request.SourceSessionId))!.Status);
        Assert.Equal("Closed", (await db.TableSessions.FindAsync(prepared.TargetSessionId))!.Status);
        Assert.False(await db.SessionTables.AnyAsync(value => value.TableSessionId == prepared.TargetSessionId && value.ReleasedAt == null));
    }

    [Fact]
    public async Task TransferRejectsChangedPayloadUnauthorizedRoleAndForgedFinish()
    {
        var (request, _) = await TransferSetup(false);
        using var channel = TransferChannel();
        var client = new TableTransferSessionGrpc.TableTransferSessionGrpcClient(channel);
        var denied = await Assert.ThrowsAsync<RpcException>(async () =>
            await client.PrepareAsync(TransferInput(request), new Metadata { { "authorization", "Bearer kitchen" } }));
        Assert.Equal(StatusCode.PermissionDenied, denied.StatusCode);
        await client.PrepareAsync(TransferInput(request), TransferHeaders());
        request.Reason = "Changed request";
        Assert.Equal(StatusCode.FailedPrecondition, (await Assert.ThrowsAsync<RpcException>(async () =>
            await client.PrepareAsync(TransferInput(request), TransferHeaders()))).StatusCode);
        Assert.Equal(StatusCode.PermissionDenied, (await Assert.ThrowsAsync<RpcException>(async () =>
            await client.FinishAsync(new TableTransferFinishRequest
            {
                OperationId = request.OperationId.ToString()
            }, TransferHeaders()))).StatusCode);
    }

    [Fact]
    public async Task TransferRejectsDifferentRestaurantTarget()
    {
        var (request, _) = await TransferSetup(false);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "admin");
        var other = await CreateRestaurant();
        var area = await CreateArea(other.RestaurantId, "OTHER");
        var table = await CreateTable(area.AreaId);
        request.TargetTableId = table.TableId;
        using var channel = TransferChannel();
        var client = new TableTransferSessionGrpc.TableTransferSessionGrpcClient(channel);
        Assert.Equal(StatusCode.PermissionDenied, (await Assert.ThrowsAsync<RpcException>(async () =>
            await client.PrepareAsync(TransferInput(request), TransferHeaders()))).StatusCode);
        using var scope = _restaurant.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
        Assert.Empty(await db.TableTransfers.ToListAsync());
        Assert.Equal("Open", (await db.TableSessions.FindAsync(request.SourceSessionId))!.Status);
    }

    private async Task<(TableTransferDTO Request, Guid? Target)> TransferSetup(bool merge)
    {
        var restaurant = await CreateRestaurant();
        await AssignWaiter(restaurant.RestaurantId);
        var area = await CreateArea(restaurant.RestaurantId, "TRANSFER");
        var source = await CreateTable(area.AreaId);
        var target = await Read<TableDto>(await _http.PostAsJsonAsync("/api/tables", new TableWriteDto
        {
            AreaId = area.AreaId,
            TableCode = "T2",
            Capacity = 4
        }), System.Net.HttpStatusCode.Created);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "waiter");
        var opened = await Read<TableSessionScanDto>(await _http.PostAsync($"/api/table-sessions/tables/{source.TableId}/open", null));
        Guid? targetId = null;
        if (merge)
        {
            targetId = (await Read<TableSessionScanDto>(await _http.PostAsync($"/api/table-sessions/tables/{target.TableId}/open", null))).TableSessionId;
        }
        return (new TableTransferDTO
        {
            OperationId = Guid.NewGuid(),
            SourceSessionId = opened.TableSessionId,
            TargetTableId = target.TableId,
            Mode = merge ? "Merge" : "Split",
            Reason = "Khách yêu cầu chuyển bàn",
            Items = merge ? [] : [new TableTransferLineDTO
            {
                OrderItemId = Guid.NewGuid(),
                Quantity = 1
            }]
        }, targetId);
    }

    private GrpcChannel TransferChannel()
    {
        return GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = _restaurant.Server.CreateHandler()
        });
    }

    private static Metadata TransferHeaders(bool internalCall = false)
    {
        var headers = new Metadata { { "authorization", "Bearer waiter" } };
        if (internalCall)
        {
            headers.Add("x-table-transfer-key", "table-transfer-test-key");
        }
        return headers;
    }

    private static TableTransferSessionRequest TransferInput(TableTransferDTO request)
    {
        return new TableTransferSessionRequest
        {
            Payload = JsonSerializer.Serialize(request)
        };
    }

    private static TableTransferResultDTO TransferRead(TableTransferSessionReply reply)
    {
        return JsonSerializer.Deserialize<TableTransferResultDTO>(reply.Payload)!;
    }
}
