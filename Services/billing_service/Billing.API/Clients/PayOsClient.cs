using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Billing.Common.Settings;
using Microsoft.Extensions.Options;

namespace Billing.API.Clients;

public sealed class PayOsClient(HttpClient http, IOptions<PayOsSettings> options, IHostEnvironment environment) : IPayOsClient
{
    public bool IsConfigured
    {
        get
        {
            var s = options.Value;
            return !string.IsNullOrWhiteSpace(s.ClientId) && !string.IsNullOrWhiteSpace(s.ApiKey)
                && !string.IsNullOrWhiteSpace(s.ChecksumKey) && ValidUrl(s.BaseUrl)
                && s.RequestTimeoutSeconds is > 0 and <= 120 && s.ReconcileIntervalSeconds > 0
                && s.ReconcileBatchSize is > 0 and <= 100 && s.PaidReconcileHours is > 0 and <= 8760;
        }
    }

    public void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new BillingException(409, "Bật PayOS và cấu hình thông tin xác thực, đường dẫn HTTPS và giới hạn trên máy chủ.");
        }
    }

    public void EnsureCanCreate()
    {
        EnsureConfigured();
        var s = options.Value;
        if (!ValidUrl(s.ReturnUrl) || !ValidUrl(s.CancelUrl) || !ValidUrl(s.WebhookUrl) || s.LinkLifetimeMinutes <= 0)
        {
            throw new BillingException(409, "Cấu hình các đường dẫn HTTPS return, cancel, webhook và hạn liên kết PayOS.");
        }
    }

    public Task<PayOsProviderResult> CreateAsync(PayOsCreateCommand request, CancellationToken ct)
    {
        EnsureCanCreate();
        var s = options.Value;
        return Call(HttpMethod.Post, "/v2/payment-requests", new
        {
            orderCode = request.OrderCode,
            amount = request.Amount,
            description = request.Description,
            cancelUrl = s.CancelUrl,
            returnUrl = s.ReturnUrl,
            expiredAt = request.ExpiresAt.ToUnixTimeSeconds(),
            signature = PayOsSignature.SignPaymentRequest(request.OrderCode, request.Amount, request.Description, s.CancelUrl, s.ReturnUrl, s.ChecksumKey)
        }, request.OrderCode, true, ct);
    }

    public Task<PayOsProviderResult> GetAsync(long orderCode, CancellationToken ct)
    {
        return Call(HttpMethod.Get, "/v2/payment-requests/" + orderCode.ToString(CultureInfo.InvariantCulture), null, orderCode, false, ct);
    }

    public Task<PayOsProviderResult> CancelAsync(long orderCode, string reason, CancellationToken ct)
    {
        return Call(HttpMethod.Post, "/v2/payment-requests/" + orderCode.ToString(CultureInfo.InvariantCulture) + "/cancel",
            new
            {
                cancellationReason = reason
            }, orderCode, false, ct);
    }

    public PayOsWebhookDTO VerifyWebhook(string payload)
    {
        EnsureConfigured();
        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions
            {
                MaxDepth = 32
            });
            var root = document.RootElement;
            DistinctRoot(root);
            var data = root.GetProperty("data");
            if (!PayOsSignature.VerifyData(data, Text(root, "signature"), options.Value.ChecksumKey)
                || Text(root, "code") != "00" || !root.GetProperty("success").GetBoolean())
            {
                throw new BillingException(400, "Chữ ký hoặc dữ liệu webhook PayOS không hợp lệ.");
            }

            var reference = Text(data, "reference");
            var order = Number(data, "orderCode");
            if (order <= 0 || reference.Length is 0 or > 255 || reference.Any(value => value is < ' ' or > '~'))
            {
                throw new JsonException();
            }

            var dedupe = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("PayOS:" + reference))).ToLowerInvariant();
            return new PayOsWebhookDTO(order, Number(data, "amount"), reference, Text(data, "paymentLinkId"),
                Text(data, "currency"), Text(data, "code"), PayOsSignature.PayloadHash(JsonSerializer.SerializeToElement(new
                {
                    orderCode = order,
                    amount = Number(data, "amount"),
                    reference,
                    paymentLinkId = Text(data, "paymentLinkId"),
                    currency = Text(data, "currency"),
                    code = Text(data, "code")
                })), dedupe);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new BillingException(400, "Dữ liệu webhook PayOS sai định dạng hoặc không thể xác minh.");
        }
    }

    private async Task<PayOsProviderResult> Call(HttpMethod method, string path, object? body, long order, bool creating, CancellationToken ct)
    {
        EnsureConfigured();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.RequestTimeoutSeconds));
        using var request = new HttpRequestMessage(method, options.Value.BaseUrl.TrimEnd('/') + path);
        request.Headers.Add("x-client-id", options.Value.ClientId);
        request.Headers.Add("x-api-key", options.Value.ApiKey);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        try
        {
            using var response = await http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new PayOsClientException("HttpError", ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
            }

            var raw = await response.Content.ReadAsStringAsync(timeout.Token);
            if (raw.Length > 262144)
            {
                throw new PayOsClientException("OversizedResponse");
            }

            using var document = JsonDocument.Parse(raw, new JsonDocumentOptions
            {
                MaxDepth = 32
            });
            var root = document.RootElement;
            DistinctRoot(root);
            if (Text(root, "code") != "00")
            {
                throw new PayOsClientException("ProviderRejected", Text(root, "code"));
            }

            var data = root.GetProperty("data");
            if (!PayOsSignature.VerifyData(data, Text(root, "signature"), options.Value.ChecksumKey) || Number(data, "orderCode") != order)
            {
                throw new PayOsClientException("InvalidResponseSignature");
            }

            var amount = Number(data, "amount");
            var status = Text(data, "status");
            var id = Text(data, creating ? "paymentLinkId" : "id");
            if (amount <= 0 || id.Length is 0 or > 100 || id.Any(char.IsControl)
                || status is not ("PENDING" or "PROCESSING" or "UNDERPAID" or "PAID" or "CANCELLED" or "EXPIRED" or "FAILED"))
            {
                throw new JsonException();
            }

            if (creating)
            {
                var url = Text(data, "checkoutUrl");
                var qr = Text(data, "qrCode");
                if (Text(data, "currency") != "VND" || url.Length > 2048 || !ValidUrl(url)
                    || qr.Length is 0 or > 65536 || qr.Any(value => value > 127 || char.IsControl(value)))
                {
                    throw new JsonException();
                }

                return new PayOsProviderResult(order, amount, status, id, null, null, null, url, qr);
            }

            if (data.TryGetProperty("currency", out var currency) && currency.GetString() != "VND")
            {
                throw new JsonException();
            }

            var paid = Number(data, "amountPaid");
            var remaining = Number(data, "amountRemaining");
            var transactions = data.GetProperty("transactions");
            var list = new List<PayOsTransactionDTO>();
            if (transactions.ValueKind == JsonValueKind.Array)
            {
                foreach (var transaction in transactions.EnumerateArray())
                {
                    list.Add(new PayOsTransactionDTO(Text(transaction, "reference"), Number(transaction, "amount")));
                }
            }
            else if (transactions.ValueKind != JsonValueKind.Object || transactions.EnumerateObject().Any() || paid != 0)
            {
                throw new JsonException();
            }

            return new PayOsProviderResult(order, amount, status, id, paid, remaining, list);
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new PayOsClientException("OutcomeUnknown");
        }
    }

    private bool ValidUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.UserInfo.Length == 0
            && (uri.Scheme == Uri.UriSchemeHttps || (options.Value.AllowInsecureTestTransport
                && environment.IsEnvironment("PayOsTest") && uri.Scheme == Uri.UriSchemeHttp));
    }

    private static long Number(JsonElement value, string name)
    {
        return value.GetProperty(name).GetInt64();
    }

    private static string Text(JsonElement value, string name)
    {
        return value.GetProperty(name).GetString() ?? throw new JsonException();
    }

    private static void DistinctRoot(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != value.EnumerateObject().Count())
        {
            throw new JsonException();
        }
    }
}
