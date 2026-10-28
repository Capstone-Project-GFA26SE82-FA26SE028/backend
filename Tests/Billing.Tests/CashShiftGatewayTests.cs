extern alias Gateway;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Wire = Gateway::Backend.Contracts.Billing;

namespace Billing.Tests;

public sealed class CashShiftGatewayTests
{
    [Fact]
    public async Task OpeningForwardsExplicitCashAndBearerAndReturnsCreated()
    {
        var registerId = Guid.NewGuid();
        var shiftId = Guid.NewGuid();
        var calls = 0;
        var invoker = new BillingUnaryInvoker((method, request, options) =>
        {
            calls++;
            Assert.EndsWith("/Open", method);
            Assert.Equal("Bearer cashier-session", Assert.Single(options.Headers!).Value);
            var body = Assert.IsType<Wire.OpenCashShiftRequest>(request);
            Assert.Equal(registerId.ToString(), body.CashRegisterId);
            Assert.True(body.HasOpeningCash);
            Assert.Equal("1234.56", body.OpeningCash);
            return new Wire.CashShiftInfo
            {
                CashShiftId = shiftId.ToString(),
                CashRegisterId = registerId.ToString(),
                RestaurantId = Guid.NewGuid().ToString(),
                CashierUserRef = Guid.NewGuid().ToString(),
                OpeningCash = body.OpeningCash,
                OpenedAt = "2026-10-26T04:00:00+00:00",
                Status = "Active"
            };
        });
        await using var gateway = await Start(invoker);
        using var client = gateway.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "cashier-session");
        using var response = await client.PostAsJsonAsync($"/api/cash-registers/{registerId}/shifts/open",
            new Gateway::ApiGateway.DTOs.Billing.OpenCashShiftDTO
            {
                OpeningCash = 1234.56m
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(shiftId.ToString(), json.RootElement.GetProperty("result").GetProperty("cashShiftId").GetString());
        Assert.Equal(1234.56m, json.RootElement.GetProperty("result").GetProperty("openingCash").GetDecimal());
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task MissingOpeningCashIsNotSilentlyConvertedToZero()
    {
        await using var gateway = await Start(new BillingUnaryInvoker((_, request, _) =>
        {
            Assert.False(Assert.IsType<Wire.OpenCashShiftRequest>(request).HasOpeningCash);
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Opening cash is required."));
        }));
        using var client = gateway.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "cashier-session");
        using var response = await client.PostAsJsonAsync($"/api/cash-registers/{Guid.NewGuid()}/shifts/open",
            new Gateway::ApiGateway.DTOs.Billing.OpenCashShiftDTO());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(StatusCode.Unauthenticated, 401)]
    [InlineData(StatusCode.PermissionDenied, 403)]
    [InlineData(StatusCode.NotFound, 404)]
    [InlineData(StatusCode.AlreadyExists, 409)]
    [InlineData(StatusCode.Unavailable, 503)]
    [InlineData(StatusCode.DeadlineExceeded, 504)]
    public async Task OpeningMapsBusinessAndDependencyErrors(StatusCode status, int expected)
    {
        await using var gateway = await Start(new BillingUnaryInvoker((_, _, _) =>
            throw new RpcException(new Status(status, "private-detail"))));
        using var client = gateway.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "cashier-session");
        using var response = await client.PostAsJsonAsync($"/api/cash-registers/{Guid.NewGuid()}/shifts/open",
            new Gateway::ApiGateway.DTOs.Billing.OpenCashShiftDTO
            {
                OpeningCash = 0
            });
        Assert.Equal(expected, (int)response.StatusCode);
        if (expected >= 500)
        {
            Assert.DoesNotContain("private-detail", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task OpeningWithoutBearerDoesNotCallBilling()
    {
        var calls = 0;
        await using var gateway = await Start(new BillingUnaryInvoker((_, _, _) =>
        {
            calls++;
            throw new InvalidOperationException();
        }));
        using var client = gateway.GetTestClient();
        using var response = await client.PostAsJsonAsync($"/api/cash-registers/{Guid.NewGuid()}/shifts/open",
            new Gateway::ApiGateway.DTOs.Billing.OpenCashShiftDTO
            {
                OpeningCash = 0
            });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task CurrentShiftForwardsRegisterAndBearerWithoutOpeningAShift()
    {
        var registerId = Guid.NewGuid();
        await using var gateway = await Start(new BillingUnaryInvoker((method, request, options) =>
        {
            Assert.EndsWith("/GetCurrent", method);
            Assert.Equal(registerId.ToString(), Assert.IsType<Wire.CashShiftRegisterRequest>(request).CashRegisterId);
            Assert.Equal("Bearer manager-session", Assert.Single(options.Headers!).Value);
            return new Wire.CashShiftInfo
            {
                CashShiftId = Guid.NewGuid().ToString(),
                CashRegisterId = registerId.ToString(),
                RestaurantId = Guid.NewGuid().ToString(),
                CashierUserRef = Guid.NewGuid().ToString(),
                OpeningCash = "0",
                OpenedAt = "2026-10-26T04:00:00+00:00",
                Status = "Active"
            };
        }));
        using var client = gateway.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "manager-session");
        using var response = await client.GetAsync($"/api/cash-registers/{registerId}/shifts/current");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(StatusCode.Unauthenticated, 401)]
    [InlineData(StatusCode.PermissionDenied, 403)]
    [InlineData(StatusCode.NotFound, 404)]
    [InlineData(StatusCode.AlreadyExists, 409)]
    public async Task CurrentShiftMapsPermissionAndAbsenceErrors(StatusCode status, int expected)
    {
        await using var gateway = await Start(new BillingUnaryInvoker((_, _, _) =>
            throw new RpcException(new Status(status, "Shift unavailable."))));
        using var client = gateway.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "staff-session");
        using var response = await client.GetAsync($"/api/cash-registers/{Guid.NewGuid()}/shifts/current");
        Assert.Equal(expected, (int)response.StatusCode);
    }

    private static async Task<WebApplication> Start(CallInvoker invoker)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(Gateway::ApiGateway.ApiAssembly).Assembly);
        builder.Services.Configure<Gateway::ApiGateway.Settings.BillingClientSettings>(settings =>
        {
            settings.TimeoutSeconds = 15;
        });
        builder.Services.AddSingleton(new Wire.CashShiftGrpc.CashShiftGrpcClient(invoker));
        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }
}
