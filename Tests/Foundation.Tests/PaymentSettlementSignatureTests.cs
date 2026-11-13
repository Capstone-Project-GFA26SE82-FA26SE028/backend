using Shared.Common.Billing;
using Xunit;

namespace Foundation.Tests;

public sealed class PaymentSettlementSignatureTests
{
    private const string Key = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";

    [Fact]
    public void SignatureBindsPurposeAllIdentifiersPaidTimeAndExpiry()
    {
        var now = DateTimeOffset.UtcNow;
        var payment = new PaidSessionSettlement(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now.AddMinutes(-1));
        var timestamp = now.ToUnixTimeSeconds();
        var signature = PaymentSettlementSignature.Sign(PaymentSettlementSignature.ProofPurpose, PaymentSettlementSignature.Proof(payment), timestamp, Key);
        Assert.True(PaymentSettlementSignature.Verify(PaymentSettlementSignature.ProofPurpose, PaymentSettlementSignature.Proof(payment), timestamp, signature, Key, now));
        foreach (var changed in new[]
        {
            payment with { TableSessionId = Guid.NewGuid() },
            payment with { RestaurantId = Guid.NewGuid() },
            payment with { BillId = Guid.NewGuid() },
            payment with { CheckoutId = Guid.NewGuid() },
            payment with { PaidAt = now }
        })
        {
            Assert.False(PaymentSettlementSignature.Verify(PaymentSettlementSignature.ProofPurpose, PaymentSettlementSignature.Proof(changed), timestamp, signature, Key, now));
        }

        Assert.False(PaymentSettlementSignature.Verify(PaymentSettlementSignature.ReadPurpose, PaymentSettlementSignature.Proof(payment), timestamp, signature, Key, now));
        Assert.False(PaymentSettlementSignature.Verify(PaymentSettlementSignature.ProofPurpose, PaymentSettlementSignature.Proof(payment), timestamp, signature, Key, now.AddSeconds(61)));
        Assert.False(PaymentSettlementSignature.Verify(PaymentSettlementSignature.ProofPurpose, PaymentSettlementSignature.Proof(payment), timestamp, signature, Key, now.AddSeconds(-61)));
        Assert.False(PaymentSettlementSignature.Verify(PaymentSettlementSignature.ProofPurpose, PaymentSettlementSignature.Proof(payment), timestamp, new string('X', 64), Key, now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-base64-key")]
    [InlineData("AQID")]
    public void MissingOrWeakKeyCannotAuthorizeClosing(string key)
    {
        Assert.False(PaymentSettlementSignature.IsConfigured(key));
        Assert.Throws<InvalidOperationException>(() => PaymentSettlementSignature.Sign(PaymentSettlementSignature.ReadPurpose, "scope", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), key));
    }
}
