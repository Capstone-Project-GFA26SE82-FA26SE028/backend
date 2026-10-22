using Backend.Contracts.Inventory;
using Catalog.API.Authorization;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Catalog.Tests;

public sealed class InventoryRecipeLookupTests
{
    [Fact]
    public async Task ForwardsTokenAndUsesVerifiedFactor()
    {
        var client = new Client();
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Bearer manager-session";
        var lookup = new InventoryRecipeLookup(client, new HttpContextAccessor
        {
            HttpContext = http
        });
        var result = await lookup.ResolveAsync(Guid.NewGuid(), Guid.NewGuid(), default);
        Assert.Null(result.Error);
        Assert.Equal(1000, result.Factor);
        Assert.Equal("Bearer manager-session", client.Authorization);
        Assert.NotNull(client.Deadline);
    }

    [Theory]
    [InlineData(StatusCode.NotFound, 404)]
    [InlineData(StatusCode.Aborted, 409)]
    [InlineData(StatusCode.PermissionDenied, 403)]
    [InlineData(StatusCode.Unauthenticated, 401)]
    [InlineData(StatusCode.Unavailable, 503)]
    [InlineData(StatusCode.DeadlineExceeded, 504)]
    public async Task DependencyErrorsAreNotConvertedToDefaultFactor(StatusCode status, int expected)
    {
        var lookup = new InventoryRecipeLookup(new Client
        {
            Failure = status
        }, new HttpContextAccessor());
        var result = await lookup.ResolveAsync(Guid.NewGuid(), Guid.NewGuid(), default);
        Assert.Equal(expected, result.Error!.StatusCode);
        Assert.Equal(0, result.Factor);
    }

    [Fact]
    public async Task RejectsResponseForAnotherIngredient()
    {
        var lookup = new InventoryRecipeLookup(new Client
        {
            WrongIngredient = true
        }, new HttpContextAccessor());
        var result = await lookup.ResolveAsync(Guid.NewGuid(), Guid.NewGuid(), default);
        Assert.Equal(503, result.Error!.StatusCode);
    }

    private sealed class Client : IngredientUnitGrpc.IngredientUnitGrpcClient
    {
        public StatusCode? Failure { get; set; }
        public bool WrongIngredient { get; set; }
        public string? Authorization { get; private set; }
        public DateTime? Deadline { get; private set; }

        public override AsyncUnaryCall<ConversionInfo> ResolveAsync(ConversionRequest request, Metadata? headers = null,
            DateTime? deadline = null, CancellationToken cancellationToken = default)
        {
            Authorization = headers?.GetValue("authorization");
            Deadline = deadline;
            if (Failure.HasValue)
            {
                throw new RpcException(new Status(Failure.Value, "Inventory error"));
            }
            return new AsyncUnaryCall<ConversionInfo>(Task.FromResult(new ConversionInfo
            {
                IngredientId = WrongIngredient ? Guid.NewGuid().ToString() : request.IngredientId,
                UnitId = request.UnitId,
                Factor = "1000"
            }), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () =>
            {
            });
        }
    }
}
