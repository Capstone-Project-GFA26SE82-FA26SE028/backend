extern alias Gateway;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Billing.API.GrpcServices;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shared.Common.Pagination;
using GatewayBilling = Gateway::Backend.Contracts.Billing;

namespace Billing.Tests;

public sealed class BillingApiBoundaryTests
{
    [Theory]
    [InlineData("create")]
    [InlineData("list")]
    [InlineData("detail")]
    public async Task GrpcRejectsMalformedIdsBeforeCallingBusinessService(string operation)
    {
        var service = new RegisterService();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddGrpc(options => options.Interceptors.Add<BillingInterceptor>());
        builder.Services.AddSingleton<ICashRegisterService>(service);
        await using var app = builder.Build();
        app.MapGrpcService<CashRegisterGrpcService>();
        await app.StartAsync();
        using var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = app.GetTestServer().CreateHandler()
        });
        var client = new GatewayBilling.CashRegisterGrpc.CashRegisterGrpcClient(channel);
        Task Call()
        {
            return operation switch
            {
                "create" => client.CreateAsync(new GatewayBilling.CreateCashRegisterRequest
                {
                    RestaurantId = "not-a-guid",
                    Code = "MAIN"
                }).ResponseAsync,
                "list" => client.GetAllAsync(new GatewayBilling.CashRegisterListRequest
                {
                    RestaurantId = Guid.Empty.ToString(),
                    Page = 1,
                    PageSize = 20
                }).ResponseAsync,
                _ => client.GetByIdAsync(new GatewayBilling.CashRegisterIdRequest
                {
                    CashRegisterId = "not-a-guid"
                }).ResponseAsync
            };
        }

        var error = await Assert.ThrowsAsync<RpcException>(Call);
        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task GatewayForwardsEmployeeTokenAndScopeAcrossCreateListAndDetail()
    {
        var branch = Guid.NewGuid();
        var register = Guid.NewGuid();
        var methods = new List<string>();
        var invoker = new BillingUnaryInvoker((method, request, options) =>
        {
            methods.Add(method);
            Assert.Equal("Bearer employee-session", options.Headers!.Single(value => value.Key == "authorization").Value);
            Assert.InRange(options.Deadline!.Value - DateTime.UtcNow, TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(16));
            var info = new GatewayBilling.CashRegisterInfo
            {
                CashRegisterId = register.ToString(),
                RestaurantId = branch.ToString(),
                Code = "MAIN"
            };
            if (request is GatewayBilling.CreateCashRegisterRequest create)
            {
                Assert.Equal(branch.ToString(), create.RestaurantId);
                Assert.Equal("MAIN", create.Code);
                return info;
            }

            if (request is GatewayBilling.CashRegisterListRequest query)
            {
                Assert.Equal(branch.ToString(), query.RestaurantId);
                Assert.Equal("MAIN", query.Search);
                Assert.Equal(2, query.Page);
                Assert.Equal(5, query.PageSize);
                var page = new GatewayBilling.CashRegisterPage
                {
                    TotalCount = 6,
                    Page = 2,
                    PageSize = 5
                };
                page.Items.Add(info);
                return page;
            }

            Assert.Equal(register.ToString(), Assert.IsType<GatewayBilling.CashRegisterIdRequest>(request).CashRegisterId);
            return info;
        });
        await using var app = await StartGateway(invoker);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "employee-session");
        using var created = await client.PostAsJsonAsync("/api/cash-registers", new
        {
            restaurantId = branch,
            code = "MAIN"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("no-store", created.Headers.CacheControl!.ToString());
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.Equal(register, createdBody.RootElement.GetProperty("result").GetProperty("cashRegisterId").GetGuid());
        using var listed = await client.GetAsync($"/api/cash-registers?restaurantId={branch}&search=MAIN&page=2&pageSize=5");
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        using var listBody = JsonDocument.Parse(await listed.Content.ReadAsStringAsync());
        Assert.Equal(6, listBody.RootElement.GetProperty("result").GetProperty("totalCount").GetInt32());
        using var detail = await client.GetAsync($"/api/cash-registers/{register}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(new[]
        {
            "/billing.CashRegisterGrpc/Create",
            "/billing.CashRegisterGrpc/GetAll",
            "/billing.CashRegisterGrpc/GetById"
        }, methods);
    }

    [Fact]
    public async Task EveryRegisterRouteRequiresAnEmployeeTokenBeforeGrpc()
    {
        var calls = 0;
        await using var app = await StartGateway(new BillingUnaryInvoker((_, _, _) =>
        {
            calls++;
            throw new InvalidOperationException("Unauthenticated requests must not reach Billing.");
        }));
        using var client = app.GetTestClient();
        using var created = await client.PostAsJsonAsync("/api/cash-registers", new
        {
            restaurantId = Guid.NewGuid(),
            code = "MAIN"
        });
        using var listed = await client.GetAsync("/api/cash-registers");
        using var detail = await client.GetAsync($"/api/cash-registers/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, created.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, listed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, detail.StatusCode);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(StatusCode.InvalidArgument, 400)]
    [InlineData(StatusCode.Unauthenticated, 401)]
    [InlineData(StatusCode.PermissionDenied, 403)]
    [InlineData(StatusCode.NotFound, 404)]
    [InlineData(StatusCode.AlreadyExists, 409)]
    [InlineData(StatusCode.Aborted, 409)]
    [InlineData(StatusCode.FailedPrecondition, 409)]
    [InlineData(StatusCode.Unavailable, 503)]
    [InlineData(StatusCode.DeadlineExceeded, 504)]
    [InlineData(StatusCode.Internal, 502)]
    public async Task GatewayPreservesControlledFailureStatusAndRedactsDependencyFailures(StatusCode status, int expected)
    {
        await using var app = await StartGateway(new BillingUnaryInvoker((_, _, _) =>
        {
            throw new RpcException(new Status(status, "private-dependency-detail"));
        }));
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "employee-session");
        using var response = await client.GetAsync("/api/cash-registers");
        Assert.Equal(expected, (int)response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, body.RootElement.GetProperty("statusCode").GetInt32());
        Assert.False(body.RootElement.GetProperty("isSuccess").GetBoolean());
        if (expected is 502 or 503 or 504)
        {
            Assert.DoesNotContain("private-dependency-detail", body.RootElement.GetProperty("message").GetString());
        }
    }

    private static async Task<WebApplication> StartGateway(CallInvoker invoker)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(Gateway::ApiGateway.ApiAssembly).Assembly);
        builder.Services.Configure<Gateway::ApiGateway.Settings.BillingClientSettings>(settings =>
        {
            settings.TimeoutSeconds = 15;
        });
        builder.Services.AddSingleton(new GatewayBilling.CashRegisterGrpc.CashRegisterGrpcClient(invoker));
        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }

    private sealed class RegisterService : ICashRegisterService
    {
        public int Calls { get; private set; }

        public Task<CashRegisterDTO> Create(CreateCashRegisterDTO request, CancellationToken ct)
        {
            Calls++;
            throw new InvalidOperationException("Malformed IDs must be rejected before the business service.");
        }

        public Task<PagedResult<CashRegisterDTO>> GetAll(CashRegisterQueryDTO query, CancellationToken ct)
        {
            Calls++;
            throw new InvalidOperationException("Malformed IDs must be rejected before the business service.");
        }

        public Task<PagedResult<CashRegisterDTO>> GetAvailableForCheckout(CashRegisterQueryDTO query, CancellationToken ct)
        {
            Calls++;
            throw new InvalidOperationException("Invalid queries must be rejected before the business service.");
        }

        public Task<CashRegisterDTO> GetById(Guid id, CancellationToken ct)
        {
            Calls++;
            throw new InvalidOperationException("Malformed IDs must be rejected before the business service.");
        }
    }
}
