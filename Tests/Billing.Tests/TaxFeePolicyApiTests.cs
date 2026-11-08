extern alias Gateway;

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
using Wire = Gateway::Backend.Contracts.Billing;

namespace Billing.Tests;

public sealed class TaxFeePolicyApiTests
{
    [Theory]
    [InlineData("NaN")]
    [InlineData("0,08")]
    [InlineData("1e-2")]
    [InlineData("not-a-rate")]
    public async Task GrpcRejectsMalformedDecimalsBeforeCallingBusinessService(string value)
    {
        var service = new PolicyService();
        await using var app = await Start(service);
        using var channel = Channel(app);
        var client = new Wire.TaxFeePolicyGrpc.TaxFeePolicyGrpcClient(channel);
        var error = await Assert.ThrowsAsync<RpcException>(async () => await client.CreateAsync(new Wire.CreateTaxFeePolicyRequest
        {
            VatRate = value,
            ServiceRate = "0",
            ServiceEnabled = false
        }));
        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
        Assert.Equal(0, service.Calls);
    }

    [Theory]
    [InlineData("not-an-id")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task GrpcRejectsInvalidIds(string value)
    {
        var service = new PolicyService();
        await using var app = await Start(service);
        using var channel = Channel(app);
        var client = new Wire.TaxFeePolicyGrpc.TaxFeePolicyGrpcClient(channel);
        var error = await Assert.ThrowsAsync<RpcException>(async () => await client.GetByIdAsync(new Wire.TaxFeePolicyIdRequest
        {
            TaxFeePolicyId = value
        }));
        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
        Assert.Equal(0, service.Calls);
    }

    private static GrpcChannel Channel(WebApplication app)
    {
        return GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = app.GetTestServer().CreateHandler()
        });
    }

    private static async Task<WebApplication> Start(ITaxFeePolicyService service)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddGrpc(options => options.Interceptors.Add<BillingInterceptor>());
        builder.Services.AddSingleton(service);
        var app = builder.Build();
        app.MapGrpcService<TaxFeePolicyGrpcService>();
        await app.StartAsync();
        return app;
    }

    private sealed class PolicyService : ITaxFeePolicyService
    {
        public int Calls { get; private set; }

        public Task<TaxFeePolicyDTO> Create(CreateTaxFeePolicyDTO request, CancellationToken ct)
        {
            Calls++;
            throw new NotSupportedException();
        }

        public Task<TaxFeePolicyDTO> GetById(Guid id, CancellationToken ct)
        {
            Calls++;
            throw new NotSupportedException();
        }

        public Task<PagedResult<TaxFeePolicyDTO>> GetAll(TaxFeePolicyQueryDTO query, CancellationToken ct)
        {
            Calls++;
            throw new NotSupportedException();
        }
    }
}
