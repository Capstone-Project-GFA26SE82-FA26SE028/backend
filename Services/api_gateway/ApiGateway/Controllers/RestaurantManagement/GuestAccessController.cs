using System.Globalization;
using ApiGateway.DTOs.Restaurant;
using Backend.Contracts.Restaurant;
using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Controllers.RestaurantManagement;

[ApiController]
[Route("api/table-sessions/{sessionId:guid}/guest-access")]
public sealed class GuestAccessController(
    GuestAccessGrpc.GuestAccessGrpcClient client,
    Backend.Contracts.Customer.CustomerGrpc.CustomerGrpcClient customerClient) : ManagementControllerBase
{
    [HttpPost("join")]
    public Task<IActionResult> Join(Guid sessionId, JoinTableSessionDto body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var input = body.PhoneOrCode?.Trim() ?? "";
            string? customerId = null;
            string? customerName = null;
            int? availablePoints = null;
            string? loyaltyTierName = null;

            if (input.Length == 10 && input[0] == '0' && input.All(char.IsDigit))
            {
                try
                {
                    var customer = await customerClient.GetOrCreateAsync(new Backend.Contracts.Customer.CreateCustomerRequest
                    {
                        Phone = input,
                        Name = ""
                    }, Options(ct));
                    customerId = customer.CustomerId;
                    customerName = customer.CustomerName;
                    availablePoints = customer.AvailablePoints;
                    loyaltyTierName = customer.LoyaltyTierName;
                }
                catch
                {
                    // Proceed with guest access if customer lookup fails
                }
            }

            var request = new GuestJoinRequest
            {
                TableSessionId = sessionId.ToString(),
                PhoneOrCode = input
            };
            if (!string.IsNullOrEmpty(customerId))
            {
                request.CustomerId = customerId;
            }

            var reply = await client.JoinAsync(request, Options(ct));
            var result = Map(reply);
            if (!string.IsNullOrEmpty(customerId))
            {
                result.CustomerId = Guid.Parse(customerId);
                result.CustomerName = customerName;
                result.AvailablePoints = availablePoints;
                result.LoyaltyTierName = loyaltyTierName;
            }
            Response.Cookies.Append(CookieName(sessionId), reply.Token, new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/api",
                Expires = new DateTimeOffset(result.ExpiresAt)
            });
            return result;
        }, ct);
    }

    [HttpGet("me")]
    public Task<IActionResult> GetMe(Guid sessionId, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.GetMeAsync(new GuestAccessRequest
            {
                TableSessionId = sessionId.ToString(),
                Token = Request.Cookies[CookieName(sessionId)] ?? ""
            }, Options(ct));
            return Map(reply);
        }, ct);
    }

    private static string CookieName(Guid sessionId)
    {
        return $"guest_access_{sessionId:N}";
    }

    private static GuestAccessDto Map(GuestAccessReply reply)
    {
        return new GuestAccessDto
        {
            TableSessionId = Guid.Parse(reply.TableSessionId),
            ExpiresAt = DateTime.Parse(reply.ExpiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
        };
    }
}
