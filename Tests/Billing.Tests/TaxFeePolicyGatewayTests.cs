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

public sealed class TaxFeePolicyGatewayTests
{
    [Fact]
    public async Task CreateListAndDetailForwardBearerAndMapPolicyFields()
    {
        var id = Guid.NewGuid();
        var calls = new List<string>();
        await using var app = await Start(new BillingUnaryInvoker((method, request, options) =>
        {
            calls.Add(method);
            Assert.Equal("Bearer admin-session", Assert.Single(options.Headers!).Value);
            Assert.NotNull(options.Deadline);
            var info = Reply(id);
            if (request is Wire.CreateTaxFeePolicyRequest create)
            {
                Assert.True(create.HasVatRate);
                Assert.Equal("0", create.VatRate);
                Assert.Equal("0.05", create.ServiceRate);
                Assert.True(create.HasServiceEnabled);
                Assert.False(create.ServiceEnabled);
                Assert.False(create.HasEffectiveFrom);
                return info;
            }

            if (request is Wire.TaxFeePolicyListRequest list)
            {
                Assert.Equal(2, list.Page);
                Assert.Equal(5, list.PageSize);
                var page = new Wire.TaxFeePolicyPage
                {
                    TotalCount = 6,
                    Page = 2,
                    PageSize = 5
                };
                page.Items.Add(info);
                return page;
            }

            Assert.Equal(id.ToString(), Assert.IsType<Wire.TaxFeePolicyIdRequest>(request).TaxFeePolicyId);
            return info;
        }));
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "admin-session");
        using var created = await client.PostAsJsonAsync("/api/tax-fee-policies", new
        {
            VatRate = 0m,
            ServiceRate = 0.05m,
            ServiceEnabled = false,
            EffectiveFrom = (string?)null
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("no-store", created.Headers.CacheControl!.ToString());
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var result = json.RootElement.GetProperty("result");
        Assert.Equal(id.ToString(), result.GetProperty("taxFeePolicyId").GetString());
        Assert.Equal(7, result.GetProperty("version").GetInt32());
        Assert.Equal(0m, result.GetProperty("vatRate").GetDecimal());
        Assert.False(result.GetProperty("serviceEnabled").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/tax-fee-policies?page=2&pageSize=5")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/tax-fee-policies/{id}")).StatusCode);
        Assert.Equal(3, calls.Count);
    }

    [Fact]
    public async Task MissingFieldsAreForwardedAsMissingAndExplicitTimeIsUnchanged()
    {
        var calls = 0;
        await using var app = await Start(new BillingUnaryInvoker((_, request, _) =>
        {
            calls++;
            var create = Assert.IsType<Wire.CreateTaxFeePolicyRequest>(request);
            Assert.False(create.HasVatRate);
            Assert.False(create.HasServiceRate);
            Assert.False(create.HasServiceEnabled);
            Assert.Equal("2026-11-01T21:00:00+07:00", create.EffectiveFrom);
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Missing required policy fields."));
        }));
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "admin-session");
        using var result = await client.PostAsJsonAsync("/api/tax-fee-policies", new
        {
            EffectiveFrom = "2026-11-01T21:00:00+07:00"
        });
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task AnonymousRequestsDoNotReachBilling()
    {
        var calls = 0;
        await using var app = await Start(new BillingUnaryInvoker((_, _, _) =>
        {
            calls++;
            return Reply(Guid.NewGuid());
        }));
        using var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/tax-fee-policies")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/tax-fee-policies/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/tax-fee-policies", new { })).StatusCode);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task ClientCannotSupplyServerVersionOrPolicyIdAndNoUpdateDeleteRoutesExist()
    {
        var calls = 0;
        await using var app = await Start(new BillingUnaryInvoker((_, _, _) =>
        {
            calls++;
            return Reply(Guid.NewGuid());
        }));
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "admin-session");
        using var create = await client.PostAsJsonAsync("/api/tax-fee-policies", new
        {
            Version = 999,
            TaxFeePolicyId = Guid.NewGuid(),
            VatRate = 0.1,
            ServiceRate = 0,
            ServiceEnabled = false
        });
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync($"/api/tax-fee-policies/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PutAsJsonAsync($"/api/tax-fee-policies/{Guid.NewGuid()}", new { })).StatusCode);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(StatusCode.PermissionDenied, 403)]
    [InlineData(StatusCode.NotFound, 404)]
    [InlineData(StatusCode.AlreadyExists, 409)]
    [InlineData(StatusCode.Unavailable, 503)]
    [InlineData(StatusCode.DeadlineExceeded, 504)]
    public async Task ErrorsAreControlled(StatusCode status, int expected)
    {
        await using var app = await Start(new BillingUnaryInvoker((_, _, _) => throw new RpcException(new Status(status, "private-detail"))));
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "admin-session");
        using var response = await client.GetAsync("/api/tax-fee-policies");
        Assert.Equal(expected, (int)response.StatusCode);
        if (expected >= 500)
        {
            Assert.DoesNotContain("private-detail", await response.Content.ReadAsStringAsync());
        }
    }

    private static Wire.TaxFeePolicyInfo Reply(Guid id)
    {
        return new Wire.TaxFeePolicyInfo
        {
            TaxFeePolicyId = id.ToString(),
            Version = 7,
            VatRate = "0",
            ServiceRate = "0.05",
            ServiceEnabled = false,
            EffectiveFrom = "2026-10-27T14:00:00+00:00"
        };
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
        builder.Services.AddSingleton(new Wire.TaxFeePolicyGrpc.TaxFeePolicyGrpcClient(invoker));
        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }
}
