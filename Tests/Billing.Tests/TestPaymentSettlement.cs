using Billing.BLL.Implements;
using Billing.BLL.Interfaces;
using Billing.Common.Settings;
using Billing.DAL.Implements;
using Billing.DAL.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.Common.Billing;
using Shared.Persistence.UnitOfWorks;

namespace Billing.Tests;

internal sealed class TestSessionSettlementClient : ISessionSettlementClient
{
    public bool Confirmed { get; set; } = true;
    public bool Throw { get; set; }
    public List<PaidSessionSettlement> Calls { get; } = [];

    public Task<bool> TryCloseAsync(PaidSessionSettlement payment, CancellationToken ct)
    {
        Calls.Add(payment);
        if (Throw)
        {
            throw new InvalidOperationException("Simulated Restaurant outage.");
        }

        return Task.FromResult(Confirmed);
    }
}

internal static class TestPaymentSettlement
{
    public static PaymentSettlementService Create(BillingDbContext db, ISessionSettlementClient? client = null)
    {
        return new PaymentSettlementService(new PaymentRepository(db), new BillRepository(db), new BillCheckoutRepository(db),
            new UnitOfWork<BillingDbContext>(db), Options.Create(new CashPaymentSettings
            {
                MethodCode = "Cash"
            }), client ?? new TestSessionSettlementClient(), NullLogger<PaymentSettlementService>.Instance);
    }
}
