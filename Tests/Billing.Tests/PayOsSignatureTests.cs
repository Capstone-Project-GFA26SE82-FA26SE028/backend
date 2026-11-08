using System.Text.Json;
using Billing.API.Clients;

namespace Billing.Tests;

public sealed class PayOsSignatureTests
{
    private const string DocumentationKey = "1a54716c8f0efb2744fb28b6e38b25da7f67a925d98bc1c18bd8faaecadd7675";
    private const string DocumentationSignature = "412e915d2871504ed31be63c8f62a149a4410d34c4c42affc9006ef9917eaa03";
    private const string DocumentationData = """
        {
          "orderCode":123,"amount":3000,"description":"VQRIO123","accountNumber":"12345678",
          "reference":"TF230204212323","transactionDateTime":"2023-02-04 18:25:00",
          "currency":"VND","paymentLinkId":"124c33293c43417ab7879e14c8d9eb18",
          "code":"00","desc":"Thành công","counterAccountBankId":"","counterAccountBankName":"",
          "counterAccountName":"","counterAccountNumber":"","virtualAccountName":"","virtualAccountNumber":""
        }
        """;

    [Fact]
    public void PaymentRequestMatchesFixedCrossLanguageVector()
    {
        var signature = PayOsSignature.SignPaymentRequest(123456, 45000, "BILL123",
            "https://merchant.example/cancel?source=qr", "https://merchant.example/return?source=qr", "test-checksum-key");
        Assert.Equal("d4e9cf86c9cb479e4010336b5b2960cd781da0b1c91ed6f0f2720e3f2c725971", signature);
    }

    [Fact]
    public void OfficialPublishedWebhookVectorMatches()
    {
        using var document = JsonDocument.Parse(DocumentationData);
        Assert.True(PayOsSignature.VerifyData(document.RootElement, DocumentationSignature, DocumentationKey));
    }

    [Theory]
    [InlineData("\"amount\":3000", "\"amount\":3001")]
    [InlineData("TF230204212323", "TF230204212324")]
    [InlineData("VND", "USD")]
    public void TamperedSignedFinancialFieldsAreRejected(string original, string changed)
    {
        using var document = JsonDocument.Parse(DocumentationData.Replace(original, changed, StringComparison.Ordinal));
        Assert.False(PayOsSignature.VerifyData(document.RootElement, DocumentationSignature, DocumentationKey));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-signature")]
    public void MissingOrMalformedSignatureCannotVerify(string? signature)
    {
        using var document = JsonDocument.Parse(DocumentationData);
        Assert.False(PayOsSignature.VerifyData(document.RootElement, signature, DocumentationKey));
    }

    [Fact]
    public void MissingConfigurationCannotVerify()
    {
        using var document = JsonDocument.Parse(DocumentationData);
        Assert.False(PayOsSignature.VerifyData(document.RootElement, DocumentationSignature, string.Empty));
    }

    [Fact]
    public void DuplicateJsonPropertiesAreRejected()
    {
        using var document = JsonDocument.Parse(DocumentationData.Replace("\"amount\":3000", "\"amount\":1,\"amount\":3000", StringComparison.Ordinal));
        Assert.False(PayOsSignature.VerifyData(document.RootElement, DocumentationSignature, DocumentationKey));
    }

    [Fact]
    public void ExtraSignedFieldCannotBeIgnored()
    {
        using var document = JsonDocument.Parse(DocumentationData.TrimEnd()[..^1] + ",\"unexpected\":\"value\"}");
        Assert.False(PayOsSignature.VerifyData(document.RootElement, DocumentationSignature, DocumentationKey));
    }
}
