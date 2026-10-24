using System.Globalization;
using ApiGateway.DTOs.AI;
using ApiGateway.Settings;
using Backend.Contracts.AI;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shared.Common.DTOs;

namespace ApiGateway.Controllers.AI;

public abstract class AssistantControllerBase(IOptions<AIClientSettings> settings) : ControllerBase
{
    protected GuestAccess Access(Guid sessionId)
    {
        return new GuestAccess
        {
            TableSessionId = sessionId.ToString(),
            GuestToken = Request.Cookies[$"guest_access_{sessionId:N}"] ?? string.Empty
        };
    }

    protected async Task<IActionResult> Execute(Func<CallOptions, Task<object>> action, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.Value.TimeoutSeconds));
        try
        {
            var value = await action(new CallOptions(deadline: DateTime.UtcNow.AddSeconds(settings.Value.TimeoutSeconds), cancellationToken: timeout.Token));
            return Ok(new ResponseDTO("Success.", 200, true, value));
        }
        catch (RpcException error) when (!ct.IsCancellationRequested)
        {
            var status = timeout.IsCancellationRequested ? 504 : error.StatusCode switch
            {
                Grpc.Core.StatusCode.InvalidArgument => 400,
                Grpc.Core.StatusCode.Unauthenticated => 401,
                Grpc.Core.StatusCode.PermissionDenied => 403,
                Grpc.Core.StatusCode.NotFound => 404,
                Grpc.Core.StatusCode.FailedPrecondition or Grpc.Core.StatusCode.Aborted => 409,
                Grpc.Core.StatusCode.OutOfRange => 413,
                Grpc.Core.StatusCode.ResourceExhausted => 429,
                Grpc.Core.StatusCode.Unavailable => 503,
                Grpc.Core.StatusCode.DeadlineExceeded => 504,
                _ => 502
            };
            var message = status switch
            {
                429 => "The assistant is busy. Please try again shortly.",
                503 => "The assistant or a required service is not ready. Please try again or ask a staff member.",
                504 => "The assistant request exceeded its time limit. Please try again.",
                502 => "The assistant could not produce a verified response.",
                _ => error.Status.Detail
            };
            return StatusCode(status, new ResponseDTO(message, status));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return StatusCode(504, new ResponseDTO("The assistant request exceeded its time limit.", 504));
        }
    }

    protected static MenuCardDTO Card(MenuCard item)
    {
        return new MenuCardDTO
        {
            ItemId = Guid.Parse(item.ItemId),
            CategoryId = Guid.Parse(item.CategoryId),
            ItemName = item.ItemName,
            CategoryName = item.CategoryName,
            Description = item.HasDescription ? item.Description : null,
            ImageUrl = item.HasImageUrl ? item.ImageUrl : null,
            Price = item.HasPrice ? decimal.Parse(item.Price, CultureInfo.InvariantCulture) : null,
            PriceId = item.HasPriceId ? Guid.Parse(item.PriceId) : null,
            Availability = item.Availability,
            CanOrder = item.CanOrder,
            UnavailableReason = item.HasUnavailableReason ? item.UnavailableReason : null,
            Channel = item.Channel,
            EvaluatedAt = DateTimeOffset.Parse(item.EvaluatedAt, CultureInfo.InvariantCulture),
            RequiresOptionSelection = item.RequiresOptionSelection
        };
    }
}
