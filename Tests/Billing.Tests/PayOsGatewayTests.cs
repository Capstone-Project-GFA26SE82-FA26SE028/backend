extern alias Gateway;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Wire = Gateway::Backend.Contracts.Billing;

namespace Billing.Tests;

public sealed class PayOsGatewayTests
{
    [Theory]
    [InlineData(true, 201)]
    [InlineData(false, 200)]
    public async Task CreateForwardsOnlyCheckoutShiftAndIdempotencyAndPreservesNullableData(bool created, int expected)
    {
        var bill = Guid.NewGuid();
        var checkout = Guid.NewGuid();
        var shift = Guid.NewGuid();
        var key = Guid.NewGuid().ToString();
        var payment = Payment(bill);
        await using var app = await Start(new BillingUnaryInvoker((method, body, options) =>
        {
            Assert.EndsWith("/Create", method);
            var request = Assert.IsType<Wire.CreatePayOsPaymentRequest>(body);
            Assert.Equal(checkout.ToString(), request.CheckoutId);
            Assert.Equal(shift.ToString(), request.CashShiftId);
            Assert.Equal(key, request.IdempotencyKey);
            Assert.Equal("Bearer cashier", Assert.Single(options.Headers!).Value);
            return new Wire.PayOsPaymentReply
            {
                Payment = payment,
                Created = created
            };
        }));
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "cashier");
        client.DefaultRequestHeaders.Add("Idempotency-Key", key);
        using var response = await client.PostAsJsonAsync($"/api/bills/{bill}/payments/payos", new
        {
            checkoutId = checkout,
            cashShiftId = shift
        });
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal(created ? $"/api/payments/{payment.PaymentId}/payos" : null, response.Headers.Location?.ToString());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = json.RootElement.GetProperty("result");
        Assert.Equal(232, result.GetProperty("amount").GetDecimal());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("providerAmountPaid").ValueKind);
        Assert.Equal(JsonValueKind.Null, result.GetProperty("qrCode").ValueKind);
        Assert.False(result.GetProperty("isVerifiedZero").GetBoolean());
    }

    [Fact]
    public async Task PaymentOperationsRequireBearerButWebhookUsesSignedPayloadInstead()
    {
        var calls = 0;
        await using var app = await Start(new BillingUnaryInvoker((method, body, options) =>
        {
            calls++;
            Assert.EndsWith("/Process", method);
            Assert.Null(options.Headers);
            Assert.Equal("{\"signature\":\"signed-data\"}", Assert.IsType<Wire.PaymentWebhookRequest>(body).Payload);
            return new Wire.PaymentWebhookReply
            {
                Received = true
            };
        }));
        using var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/payments/{Guid.NewGuid()}/payos")).StatusCode);
        using var response = await client.PostAsync("/api/payments/payos/webhook", new StringContent("{\"signature\":\"signed-data\"}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(StatusCode.InvalidArgument, 400)]
    [InlineData(StatusCode.Unavailable, 503)]
    [InlineData(StatusCode.Internal, 502)]
    public async Task WebhookErrorsRemainControlled(StatusCode status, int expected)
    {
        await using var app = await Start(new BillingUnaryInvoker((_, _, _) => throw new RpcException(new Status(status, "private-provider-error"))));
        using var client = app.GetTestClient();
        using var response = await client.PostAsJsonAsync("/api/payments/payos/webhook", new
        {
            signature = "invalid"
        });
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.DoesNotContain("private-provider-error", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task WaiterQrEndpointsForwardExactSessionCheckoutPaymentAndEmployeeToken()
    {
        var session = Guid.NewGuid();
        var checkout = Guid.NewGuid();
        var payment = Guid.NewGuid();
        var calls = 0;
        await using var app = await Start(new BillingUnaryInvoker((method, body, options) =>
        {
            calls++;
            var request = Assert.IsType<Wire.WaiterPayOsRequest>(body);
            Assert.Equal(session.ToString(), request.TableSessionId);
            Assert.Equal(checkout.ToString(), request.CheckoutId);
            Assert.Equal("Bearer waiter", Assert.Single(options.Headers!).Value);
            var value = new Wire.WaiterPayOsInfo
            {
                PaymentId = payment.ToString(),
                BillId = Guid.NewGuid().ToString(),
                TableSessionId = session.ToString(),
                CheckoutId = checkout.ToString(),
                Status = "Pending",
                BillStatus = "Unpaid",
                Amount = "232",
                CanPresentQr = false,
                EvaluatedAt = DateTimeOffset.UtcNow.ToString("O")
            };
            if (method.EndsWith("/GetForCheckout", StringComparison.Ordinal))
            {
                Assert.Equal(2, request.Page);
                Assert.Equal(1, request.PageSize);
                var page = new Wire.WaiterPayOsPage
                {
                    TotalCount = 2,
                    Page = 2,
                    PageSize = 1
                };
                page.Items.Add(value);
                return page;
            }

            Assert.EndsWith("/GetForWaiter", method);
            Assert.Equal(payment.ToString(), request.PaymentId);
            return value;
        }));
        using var client = app.GetTestClient();
        var route = $"/api/table-sessions/{session}/checkout/{checkout}/payments";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route + "/payos")).StatusCode);
        Assert.Equal(0, calls);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "waiter");
        using var response = await client.GetAsync(route + $"/{payment}/payos");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = json.RootElement.GetProperty("result");
        Assert.Equal(232, result.GetProperty("amount").GetDecimal());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("qrCode").ValueKind);
        Assert.False(result.GetProperty("paymentConfirmed").GetBoolean());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        using var pageResponse = await client.GetAsync(route + "/payos?page=2&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
        Assert.Equal(2, calls);
    }

    private static Wire.PayOsPaymentInfo Payment(Guid bill)
    {
        return new Wire.PayOsPaymentInfo
        {
            PaymentId = Guid.NewGuid().ToString(),
            BillId = bill.ToString(),
            RestaurantId = Guid.NewGuid().ToString(),
            CashShiftId = Guid.NewGuid().ToString(),
            Status = "Unknown",
            Amount = "232"
        };
    }

    private static async Task<WebApplication> Start(CallInvoker invoker)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(Gateway::ApiGateway.ApiAssembly).Assembly);
        builder.Services.Configure<Gateway::ApiGateway.Settings.PayOsGatewaySettings>(settings =>
        {
            settings.TimeoutSeconds = 45;
        });
        builder.Services.AddSingleton(new Wire.PayOsPaymentGrpc.PayOsPaymentGrpcClient(invoker));
        builder.Services.AddSingleton(new Wire.PaymentWebhookGrpc.PaymentWebhookGrpcClient(invoker));
        var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }
}
