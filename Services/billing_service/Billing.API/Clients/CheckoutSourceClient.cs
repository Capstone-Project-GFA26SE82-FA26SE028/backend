using Backend.Contracts.Ordering;
using Billing.API.Settings;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace Billing.API.Clients;

public sealed class CheckoutSourceClient(CheckoutLockGrpc.CheckoutLockGrpcClient client,
    IHttpContextAccessor http, IOptions<RestaurantAccessSettings> settings) : ICheckoutSourceClient
{
    public async Task VerifyAsync(Guid sessionId, string guestToken, CancellationToken ct)
    {
        await Call(async options =>
        {
            await client.VerifyAsync(Write(sessionId, Guid.Empty, guestToken), options);
            return true;
        }, ct);
    }

    public async Task<BillCheckoutDTO> BeginAsync(Guid sessionId, Guid id, string guestToken, CancellationToken ct)
    {
        var reply = await Call(async options => await client.BeginAsync(Write(sessionId, id, guestToken), options), ct);
        if (!Guid.TryParse(reply.CheckoutId, out var returnedId) || returnedId != id
            || !Guid.TryParse(reply.TableSessionId, out var returnedSession) || returnedSession != sessionId
            || reply.Status is not ("WaitingConfirmation" or "Frozen" or "Released")
            || (reply.Status == "Frozen" && (!reply.HasSourceHash || reply.SourceHash.Length != 64 || !reply.SourceHash.All(Uri.IsHexDigit))))
        {
            throw new BillingException(503, "Ordering returned an invalid checkout state.");
        }

        return new BillCheckoutDTO
        {
            CheckoutId = id,
            TableSessionId = sessionId,
            Status = reply.Status,
            SourceHash = reply.HasSourceHash ? reply.SourceHash : null
        };
    }

    public async Task ReleaseAsync(Guid sessionId, Guid id, CancellationToken ct)
    {
        var reply = await Call(async options => await client.ReleaseAsync(Write(sessionId, id, string.Empty), options), ct);
        if (reply.CheckoutId != id.ToString() || reply.TableSessionId != sessionId.ToString() || reply.Status != "Released")
        {
            throw new BillingException(503, "Ordering has not confirmed reopening this session.");
        }
    }

    public async Task BindAsync(Guid sessionId, Guid id, Guid billId, string hash, CancellationToken ct)
    {
        var request = Write(sessionId, id, string.Empty);
        request.BillId = billId.ToString();
        request.SourceHash = hash;
        var reply = await Call(async options => await client.BindAsync(request, options), ct);
        if (reply.CheckoutId != id.ToString() || reply.TableSessionId != sessionId.ToString()
            || reply.Status != "Frozen" || reply.SourceHash != hash)
        {
            throw new BillingException(503, "Ordering has not confirmed the frozen bill snapshot.");
        }
    }

    private async Task<T> Call<T>(Func<CallOptions, Task<T>> action, CancellationToken ct)
    {
        var headers = new Metadata();
        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(authorization))
        {
            headers.Add("authorization", authorization);
        }

        try
        {
            return await action(new CallOptions(headers, DateTime.UtcNow.AddSeconds(settings.Value.TimeoutSeconds), ct));
        }
        catch (RpcException error) when (!ct.IsCancellationRequested)
        {
            throw new BillingException(error.StatusCode switch
            {
                StatusCode.InvalidArgument => 400,
                StatusCode.Unauthenticated => 401,
                StatusCode.PermissionDenied => 403,
                StatusCode.NotFound => 404,
                StatusCode.AlreadyExists or StatusCode.FailedPrecondition or StatusCode.Aborted => 409,
                _ => 503
            }, error.StatusCode is StatusCode.Unavailable or StatusCode.Internal or StatusCode.DeadlineExceeded
                ? "Checkout outcome could not be verified. Retry the same checkout ID; orders have not been automatically reopened."
                : error.Status.Detail);
        }
    }

    private static CheckoutLockRequest Write(Guid sessionId, Guid id, string guestToken)
    {
        return new CheckoutLockRequest
        {
            TableSessionId = sessionId.ToString(),
            CheckoutId = id.ToString(),
            GuestToken = guestToken
        };
    }
}
