extern alias restaurantapi;

using System.Globalization;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Restaurant.Common.Exceptions;
using Shared.Common.Billing;
using Xunit;
using PaymentClient = restaurantapi::Backend.Contracts.Billing.PaymentSettlementGrpc.PaymentSettlementGrpcClient;
using PaymentRequest = restaurantapi::Backend.Contracts.Billing.SettlementScopeRequest;
using PaymentReply = restaurantapi::Backend.Contracts.Billing.PaidSettlementReply;
using Adapter = restaurantapi::Restaurant.API.Management.SessionPaymentClient;

namespace Restaurant.Tests;

public sealed class SessionPaymentClientTests
{
    private const string Key = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";

    [Theory]
    [InlineData(StatusCode.AlreadyExists)]
    [InlineData(StatusCode.FailedPrecondition)]
    [InlineData(StatusCode.NotFound)]
    public async Task UnpaidSessionIsNotClosed(StatusCode status)
    {
        var service = new Adapter(new Client(status), Options.Create(new PaymentSettlementSettings { SharedKey = Key }), TimeProvider.System);
        Assert.Null(await service.GetPaidAsync(Guid.NewGuid(), Guid.NewGuid(), default));
    }

    [Theory]
    [InlineData(StatusCode.Unauthenticated)]
    [InlineData(StatusCode.PermissionDenied)]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    public async Task DependencyErrorsCannotBecomePaidProof(StatusCode status)
    {
        var service = new Adapter(new Client(status), Options.Create(new PaymentSettlementSettings { SharedKey = Key }), TimeProvider.System);
        Assert.Equal(503, (await Assert.ThrowsAsync<ManagementException>(() => service.GetPaidAsync(Guid.NewGuid(), Guid.NewGuid(), default))).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlySignedResponseMatchingRequestedScopeIsAccepted(bool tampered)
    {
        var service = new Adapter(new Client(null, tampered), Options.Create(new PaymentSettlementSettings { SharedKey = Key }), TimeProvider.System);
        if (tampered)
        {
            await Assert.ThrowsAsync<ManagementException>(() => service.GetPaidAsync(Guid.NewGuid(), Guid.NewGuid(), default));
        }
        else
        {
            Assert.NotNull(await service.GetPaidAsync(Guid.NewGuid(), Guid.NewGuid(), default));
        }
    }

    private sealed class Client(StatusCode? error, bool tampered = false) : PaymentClient
    {
        public override AsyncUnaryCall<PaymentReply> GetPaidAsync(PaymentRequest request, Metadata? headers = null,
            DateTime? deadline = null, CancellationToken cancellationToken = default)
        {
            var session = Guid.Parse(request.TableSessionId);
            var restaurant = Guid.Parse(request.RestaurantId);
            Assert.NotNull(deadline);
            Assert.Null(headers?.GetValue("authorization"));
            Assert.True(PaymentSettlementSignature.Verify(PaymentSettlementSignature.ReadPurpose,
                PaymentSettlementSignature.Scope(session, restaurant), long.Parse(headers!.GetValue(PaymentSettlementSignature.TimestampHeader)!, CultureInfo.InvariantCulture),
                headers.GetValue(PaymentSettlementSignature.SignatureHeader)!, Key, DateTimeOffset.UtcNow));
            Task<PaymentReply> task;
            if (error.HasValue)
            {
                task = Task.FromException<PaymentReply>(new RpcException(new Status(error.Value, "private-provider-error")));
            }
            else
            {
                var paid = new PaidSessionSettlement(session, restaurant, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-1));
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                task = Task.FromResult(new PaymentReply
                {
                    TableSessionId = session.ToString(),
                    RestaurantId = restaurant.ToString(),
                    BillId = tampered ? Guid.NewGuid().ToString() : paid.BillId.ToString(),
                    CheckoutId = paid.CheckoutId.ToString(),
                    PaidAt = paid.PaidAt.ToString("O", CultureInfo.InvariantCulture),
                    SignatureTime = now,
                    Signature = PaymentSettlementSignature.Sign(PaymentSettlementSignature.ProofPurpose, PaymentSettlementSignature.Proof(paid), now, Key)
                });
            }

            return new AsyncUnaryCall<PaymentReply>(task, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () =>
            {
            });
        }
    }
}
