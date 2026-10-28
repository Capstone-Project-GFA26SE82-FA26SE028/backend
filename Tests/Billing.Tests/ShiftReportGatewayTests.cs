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

public sealed class ShiftReportGatewayTests
{
    [Theory]
    [InlineData(true, 201)]
    [InlineData(false, 200)]
    public async Task ClosingMapsCountBeforeRemovalAndOriginalReport(bool created, int status)
    {
        var shiftId = Guid.NewGuid();
        var report = Report(shiftId);
        await using var app = await Start(new BillingUnaryInvoker((method, input, options) =>
        {
            Assert.EndsWith("/Close", method);
            Assert.Equal("Bearer cashier", Assert.Single(options.Headers!).Value);
            var request = Assert.IsType<Wire.CloseCashShiftRequest>(input);
            Assert.Equal(shiftId.ToString(), request.CashShiftId);
            Assert.Equal("1330", request.ActualCash);
            Assert.Equal("1000", request.CashHandleOver);
            Assert.Equal("Shortage", request.DifferenceReason);
            return new Wire.CloseCashShiftReply
            {
                Report = report,
                Created = created
            };
        }));
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "cashier");
        using var response = await client.PostAsJsonAsync($"/api/cash-shifts/{shiftId}/close", new
        {
            ActualCash = 1330,
            CashHandleOver = 1000,
            DifferenceReason = "Shortage"
        });
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(created ? $"/api/shift-reports/{report.ShiftReportId}" : null, response.Headers.Location?.ToString());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = json.RootElement.GetProperty("result");
        Assert.Equal(-2, result.GetProperty("difference").GetDecimal());
        Assert.Equal(330, result.GetProperty("remainingCash").GetDecimal());
    }

    [Fact]
    public async Task ClientCannotSupplyExpectedCashOrClosingIdentity()
    {
        await using var app = await Start(new BillingUnaryInvoker((_, _, _) => throw new InvalidOperationException()));
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "cashier");
        using var response = await client.PostAsJsonAsync($"/api/cash-shifts/{Guid.NewGuid()}/close", new
        {
            ActualCash = 1000,
            CashHandleOver = 0,
            ExpectedCash = 1000,
            CashierUserRef = Guid.NewGuid()
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PreviewPreservesBlockingPaymentIdsAndRequiresBearer()
    {
        var paymentId = Guid.NewGuid();
        var shiftId = Guid.NewGuid();
        await using var app = await Start(new BillingUnaryInvoker((_, input, _) =>
        {
            Assert.Equal(shiftId.ToString(), Assert.IsType<Wire.ShiftReportShiftRequest>(input).CashShiftId);
            var reply = new Wire.ShiftReconciliationInfo
            {
                CashShiftId = shiftId.ToString(),
                RestaurantId = Guid.NewGuid().ToString(),
                Status = "Active",
                OpeningCash = "1000",
                MovementTotal = "0",
                ExpectedCash = "1000",
                SaleTotal = "0",
                BankTransferTotal = "0",
                CashInTotal = "0",
                CashOutTotal = "0",
                CanClose = false,
                PaymentCount = 1
            };
            reply.Blockers.Add(new Wire.ReconciliationBlockerInfo
            {
                Code = "UnresolvedPayment",
                PaymentId = paymentId.ToString(),
                Message = "Resolve payment."
            });
            return reply;
        }));
        using var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/cash-shifts/{shiftId}/reconciliation")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "reader");
        using var response = await client.GetAsync($"/api/cash-shifts/{shiftId}/reconciliation");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = json.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("canClose").GetBoolean());
        Assert.Equal(paymentId.ToString(), result.GetProperty("blockers")[0].GetProperty("paymentId").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("blockers")[0].GetProperty("cashMovementId").ValueKind);
    }

    [Fact]
    public async Task ReportListForwardsPaginationAndPreservesNullReason()
    {
        var restaurantId = Guid.NewGuid();
        await using var app = await Start(new BillingUnaryInvoker((method, input, _) =>
        {
            Assert.EndsWith("/GetAll", method);
            var request = Assert.IsType<Wire.ShiftReportListRequest>(input);
            Assert.Equal(restaurantId.ToString(), request.RestaurantId);
            Assert.Equal(2, request.Page);
            var result = new Wire.ShiftReportPage
            {
                TotalCount = 3,
                Page = 2,
                PageSize = 1
            };
            var report = Report(Guid.NewGuid());
            report.ClearDifferenceReason();
            result.Items.Add(report);
            return result;
        }));
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "reader");
        using var response = await client.GetAsync($"/api/shift-reports?restaurantId={restaurantId}&page=2&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("result").GetProperty("items")[0].GetProperty("differenceReason").ValueKind);
    }

    [Theory]
    [InlineData(StatusCode.PermissionDenied, 403)]
    [InlineData(StatusCode.AlreadyExists, 409)]
    [InlineData(StatusCode.Unavailable, 503)]
    public async Task ErrorsRemainControlled(StatusCode status, int expected)
    {
        await using var app = await Start(new BillingUnaryInvoker((_, _, _) => throw new RpcException(new Status(status, "private-error"))));
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "reader");
        using var response = await client.GetAsync($"/api/shift-reports/{Guid.NewGuid()}");
        Assert.Equal(expected, (int)response.StatusCode);
        if (expected >= 500)
        {
            Assert.DoesNotContain("private-error", await response.Content.ReadAsStringAsync());
        }
    }

    private static Wire.ShiftReportInfo Report(Guid shiftId)
    {
        return new Wire.ShiftReportInfo
        {
            ShiftReportId = Guid.NewGuid().ToString(),
            CashShiftId = shiftId.ToString(),
            CashRegisterId = Guid.NewGuid().ToString(),
            RestaurantId = Guid.NewGuid().ToString(),
            CashierUserRef = Guid.NewGuid().ToString(),
            PerformerSource = "ShiftOwnerDerived",
            ExpectedCash = "1332",
            ActualCash = "1330",
            CashHandleOver = "1000",
            Difference = "-2",
            RemainingCash = "330",
            DifferenceReason = "Shortage",
            ClosedAt = "2026-10-28T01:00:00Z"
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
        builder.Services.AddSingleton(new Wire.ShiftReportGrpc.ShiftReportGrpcClient(invoker));
        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }
}
