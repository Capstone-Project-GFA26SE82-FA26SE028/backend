using System.Net;
using System.Text;
using System.Text.Json;
using Billing.API.Clients;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.Common.Settings;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Billing.Tests;

public sealed class PayOsClientTests
{
    private const string Key = "public-test-key-not-a-secret";

    [Fact]
    public async Task SignedQueryRequiresExplicitPaidRemainingAndTransactions()
    {
        var data = Data();
        var provider = Client(_ => Response(data));
        var result = await provider.GetAsync(123, default);
        Assert.Equal(0, result.AmountPaid);
        Assert.Equal(232, result.AmountRemaining);
        foreach (var name in new[] { "amountPaid", "amountRemaining", "transactions", "id", "orderCode", "status" })
        {
            var incomplete = Data();
            incomplete.Remove(name);
            await Assert.ThrowsAsync<PayOsClientException>(() => Client(_ => Response(incomplete)).GetAsync(123, default));
        }
    }

    [Fact]
    public async Task CreateUsesOfficialSignatureHeadersAndConfiguredExpiry()
    {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(15);
        var provider = Client(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("test-client", request.Headers.GetValues("x-client-id").Single());
            Assert.Equal("test-api-key", request.Headers.GetValues("x-api-key").Single());
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(expiry.ToUnixTimeSeconds(), body.RootElement.GetProperty("expiredAt").GetInt64());
            Assert.Equal(PayOsSignature.SignPaymentRequest(123, 232, "000000123", "https://merchant.example/cancel",
                "https://merchant.example/return", Key), body.RootElement.GetProperty("signature").GetString());
            return Response(new Dictionary<string, object?>
            {
                ["orderCode"] = 123,
                ["amount"] = 232,
                ["status"] = "PENDING",
                ["paymentLinkId"] = "link",
                ["checkoutUrl"] = "https://pay.example/link",
                ["qrCode"] = "000QR",
                ["currency"] = "VND"
            });
        });
        var result = await provider.CreateAsync(new PayOsCreateCommand(123, 232, "000000123", expiry), default);
        Assert.Equal("000QR", result.QrCode);
        Assert.Null(result.AmountPaid);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task HttpErrorsDoNotBecomeConfirmedFailureOrExposeResponse(int status)
    {
        var provider = Client(_ => new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent("private-provider-content")
        });
        var error = await Assert.ThrowsAsync<PayOsClientException>(() => provider.GetAsync(123, default));
        Assert.DoesNotContain("private-provider", error.Message);
    }

    [Fact]
    public async Task TamperedResponseAndWrongOrderAreRejected()
    {
        var data = Data();
        var response = Response(data);
        var raw = await response.Content.ReadAsStringAsync();
        await Assert.ThrowsAsync<PayOsClientException>(() => Client(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(raw.Replace("\"amount\":232", "\"amount\":233"))
        }).GetAsync(123, default));
        await Assert.ThrowsAsync<PayOsClientException>(() => Client(_ => Response(data)).GetAsync(124, default));
    }

    [Fact]
    public async Task NetworkAndTimeoutAreUncertain()
    {
        await Assert.ThrowsAsync<PayOsClientException>(() => Client((Func<HttpRequestMessage, HttpResponseMessage>)(_ => throw new HttpRequestException())).GetAsync(123, default));
        await Assert.ThrowsAsync<PayOsClientException>(() => Client((Func<HttpRequestMessage, HttpResponseMessage>)(_ => throw new TaskCanceledException())).GetAsync(123, default));
    }

    [Fact]
    public void WebhookAuthenticatesFullDataButDedupesFinancialFieldsOnly()
    {
        var provider = Client(_ => Response(Data()));
        var data = new Dictionary<string, object?>
        {
            ["orderCode"] = 123,
            ["amount"] = 232,
            ["reference"] = "BANK-123",
            ["paymentLinkId"] = "link",
            ["currency"] = "VND",
            ["code"] = "00",
            ["description"] = "first"
        };
        var first = provider.VerifyWebhook(Webhook(data));
        data["description"] = "updated metadata";
        var second = provider.VerifyWebhook(Webhook(data));
        Assert.Equal(first.DedupeKey, second.DedupeKey);
        Assert.Equal(first.PayloadHash, second.PayloadHash);
        Assert.Throws<BillingException>(() => provider.VerifyWebhook(Webhook(data).Replace("updated metadata", "tampered metadata")));
        Assert.Throws<BillingException>(() => provider.VerifyWebhook(Webhook(data).Replace("\"success\":true", "\"success\":false")));
    }

    [Fact]
    public void HttpTransportIsTestEnvironmentOnlyAndMissingCredentialsAreControlled()
    {
        var settings = Settings();
        settings.BaseUrl = "http://payos-test";
        settings.AllowInsecureTestTransport = true;
        Assert.False(Client(_ => Response(Data()), settings, "Production").IsConfigured);
        Assert.True(Client(_ => Response(Data()), settings, "PayOsTest").IsConfigured);
        settings.ChecksumKey = "";
        Assert.Throws<BillingException>(() => Client(_ => Response(Data()), settings).EnsureConfigured());
    }

    private static Dictionary<string, object?> Data()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = "link",
            ["orderCode"] = 123,
            ["amount"] = 232,
            ["amountPaid"] = 0,
            ["amountRemaining"] = 232,
            ["status"] = "PENDING",
            ["transactions"] = Array.Empty<object>()
        };
    }

    private static HttpResponseMessage Response(object data)
    {
        var element = JsonSerializer.SerializeToElement(data);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                code = "00",
                data,
                signature = PayOsSignature.SignData(element, Key)
            }), Encoding.UTF8, "application/json")
        };
    }

    private static string Webhook(object data)
    {
        return JsonSerializer.Serialize(new
        {
            code = "00",
            success = true,
            data,
            signature = PayOsSignature.SignData(JsonSerializer.SerializeToElement(data), Key)
        });
    }

    private static PayOsSettings Settings()
    {
        return new PayOsSettings
        {
            Enabled = true,
            ClientId = "test-client",
            ApiKey = "test-api-key",
            ChecksumKey = Key,
            ReturnUrl = "https://merchant.example/return",
            CancelUrl = "https://merchant.example/cancel",
            WebhookUrl = "https://merchant.example/webhook"
        };
    }

    private static PayOsClient Client(Func<HttpRequestMessage, HttpResponseMessage> handler, PayOsSettings? settings = null, string environment = "Production")
    {
        return Client(request => Task.FromResult(handler(request)), settings, environment);
    }

    private static PayOsClient Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler, PayOsSettings? settings = null, string environment = "Production")
    {
        return new PayOsClient(new HttpClient(new Handler(handler)), Options.Create(settings ?? Settings()), new Host(environment));
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            return handler(request);
        }
    }

    private sealed class Host(string environment) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environment;
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
