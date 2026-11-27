extern alias Gateway;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Billing.API.GrpcServices;
using Billing.BLL.Implements;
using Billing.DAL.Implements;
using Billing.DAL.Models;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Persistence.UnitOfWorks;
using Wire = Gateway::Backend.Contracts.Billing;

namespace Billing.Tests;

public sealed class BillVoucherGatewayTests
{
    [Fact]
    public async Task ApplyingAndRemovingVoucherPersistsBillAndAllocations()
    {
        await using var fixture = await BillServiceTests.BillFixture.Create();
        var billId = await CreateBill(fixture);
        var voucherId = await SeedVoucher(fixture);
        fixture.Db.ChangeTracker.Clear();
        await using var gateway = await StartGateway(fixture);
        using var client = gateway.Api.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "cashier-session");
        var endpoint = $"/api/bills/{billId}";

        using var applied = await client.PostAsJsonAsync(endpoint + "/voucher", new { VoucherCode = "TEST50" });
        fixture.Db.ChangeTracker.Clear();
        var persisted = await fixture.Db.Bills.Include(item => item.BillLines).SingleAsync(item => item.BillId == billId);
        Assert.Equal(50000m, persisted.DiscountAmount);
        Assert.Equal(57750m, persisted.TotalAmount);
        Assert.Equal(2500m, persisted.ServiceAmount);
        Assert.Equal(5250m, persisted.VatAmount);
        Assert.Equal(50000m, Assert.Single(persisted.BillLines).DiscountAllocated);
        Assert.Equal(2500m, persisted.BillLines.Sum(line => line.ServiceAllocated));
        Assert.Equal(5250m, persisted.BillLines.Sum(line => line.VatAllocated));
        Assert.Single(await fixture.Db.VoucherRedemptions.Where(item => item.BillId == billId).ToListAsync());

        var response = await Read(applied);
        Assert.Equal(57750m, response.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(voucherId, response.GetProperty("adjustments")[0].GetProperty("voucherId").GetGuid());
        Assert.Equal(JsonValueKind.Null, response.GetProperty("adjustments")[0].GetProperty("promotionId").ValueKind);
        Assert.Equal(JsonValueKind.Null, response.GetProperty("adjustments")[0].GetProperty("actorUserRef").ValueKind);
        Assert.Equal(57750m, (await Read(await client.GetAsync(endpoint))).GetProperty("totalAmount").GetDecimal());

        using var duplicate = await client.PostAsJsonAsync(endpoint + "/voucher", new { VoucherCode = "TEST50" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Single(await fixture.Db.BillAdjustments.Where(item => item.BillId == billId).ToListAsync());

        var removed = await Read(await client.DeleteAsync(endpoint + $"/voucher/{voucherId}"));
        Assert.Equal(115500m, removed.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(0m, removed.GetProperty("discountAmount").GetDecimal());
        Assert.Empty(removed.GetProperty("adjustments").EnumerateArray());
        fixture.Db.ChangeTracker.Clear();
        persisted = await fixture.Db.Bills.Include(item => item.BillLines).SingleAsync(item => item.BillId == billId);
        Assert.Equal(115500m, persisted.TotalAmount);
        Assert.Equal(0m, Assert.Single(persisted.BillLines).DiscountAllocated);
        Assert.Empty(await fixture.Db.VoucherRedemptions.Where(item => item.BillId == billId).ToListAsync());
    }

    [Fact]
    public async Task BillDetailPreservesVoucherPromotionAndAbsentFields()
    {
        await using var fixture = await BillServiceTests.BillFixture.Create();
        var billId = await CreateBill(fixture);
        var voucherId = await SeedVoucher(fixture);
        var promotionId = Guid.NewGuid();
        fixture.Db.Promotions.Add(new Promotion
        {
            PromotionId = promotionId,
            PromotionName = "Test promotion",
            DiscountType = "FixedAmount",
            Type = "Order",
            DiscountValue = 5000m,
            IsActive = true,
            StartAt = DateTime.UtcNow.AddDays(-1)
        });
        fixture.Db.BillAdjustments.AddRange(
            Adjustment(billId, "Voucher", voucherId, null),
            Adjustment(billId, "Promotion", null, promotionId),
            Adjustment(billId, "Manual", null, null));
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        await using var gateway = await StartGateway(fixture);
        using var client = gateway.Api.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "cashier-session");

        var detail = await Read(await client.GetAsync($"/api/bills/{billId}"));
        var adjustments = detail.GetProperty("adjustments").EnumerateArray()
            .ToDictionary(item => item.GetProperty("type").GetString()!);
        Assert.Equal(voucherId, adjustments["Voucher"].GetProperty("voucherId").GetGuid());
        Assert.Equal(JsonValueKind.Null, adjustments["Voucher"].GetProperty("promotionId").ValueKind);
        Assert.Equal(promotionId, adjustments["Promotion"].GetProperty("promotionId").GetGuid());
        Assert.Equal(JsonValueKind.Null, adjustments["Promotion"].GetProperty("voucherId").ValueKind);
        Assert.Equal(JsonValueKind.Null, adjustments["Manual"].GetProperty("promotionId").ValueKind);
        Assert.Equal(JsonValueKind.Null, adjustments["Manual"].GetProperty("voucherId").ValueKind);
        Assert.Equal(JsonValueKind.Null, adjustments["Manual"].GetProperty("reason").ValueKind);
    }

    private static BillAdjustment Adjustment(Guid billId, string type, Guid? voucherId, Guid? promotionId)
    {
        return new BillAdjustment
        {
            BillAdjustmentId = Guid.NewGuid(),
            BillId = billId,
            VoucherId = voucherId,
            PromotionId = promotionId,
            Type = type,
            Amount = 5000
        };
    }

    private static async Task<Guid> CreateBill(BillServiceTests.BillFixture fixture)
    {
        var item = Assert.Single(fixture.Source.Value.Items).Item;
        item.BaseUnitPrice = 100000m;
        item.Options.Clear();
        return (await fixture.Service.Create(fixture.Source.Value.TableSessionId, default)).BillId;
    }

    private static async Task<Guid> SeedVoucher(BillServiceTests.BillFixture fixture)
    {
        var id = Guid.NewGuid();
        await fixture.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO Voucher (VoucherId, NormalizedVoucherCode, DiscountType, DiscountValue, StartAt, IsActive, Version)
            VALUES ({id}, 'TEST50', 'FixedAmount', 50000, {DateTime.UtcNow.AddDays(-1)}, 1, X'0000000000000000');
            """);
        return id;
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        }
    }

    private static async Task<GatewayFixture> StartGateway(BillServiceTests.BillFixture fixture)
    {
        var billing = WebApplication.CreateBuilder();
        billing.WebHost.UseTestServer();
        billing.Services.AddGrpc(options => options.Interceptors.Add<BillingInterceptor>());
        var voucherService = new VoucherService(new VoucherRepository(fixture.Db), new VoucherRedemptionRepository(fixture.Db),
            new BillAdjustmentRepository(fixture.Db), new BillRepository(fixture.Db),
            new UnitOfWork<BillingDbContext>(fixture.Db), TimeProvider.System);
        billing.Services.AddSingleton(new BillGrpcService(fixture.Service, voucherService, null!));
        var billingApp = billing.Build();
        billingApp.MapGrpcService<BillGrpcService>();
        await billingApp.StartAsync();
        var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = billingApp.GetTestServer().CreateHandler()
        });

        var gateway = WebApplication.CreateBuilder();
        gateway.WebHost.UseTestServer();
        gateway.Services.AddControllers().AddApplicationPart(typeof(Gateway::ApiGateway.ApiAssembly).Assembly);
        gateway.Services.Configure<Gateway::ApiGateway.Settings.BillingClientSettings>(options => options.TimeoutSeconds = 10);
        gateway.Services.AddSingleton(new Wire.BillGrpc.BillGrpcClient(channel));
        gateway.Services.AddSingleton(new Gateway::Backend.Contracts.Customer.CustomerGrpc.CustomerGrpcClient(channel));
        var gatewayApp = gateway.Build();
        gatewayApp.MapControllers();
        await gatewayApp.StartAsync();
        return new GatewayFixture(gatewayApp, channel, billingApp);
    }

    private sealed record GatewayFixture(WebApplication Api, GrpcChannel Channel, WebApplication Billing) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Api.DisposeAsync();
            Channel.Dispose();
            await Billing.DisposeAsync();
        }
    }
}
