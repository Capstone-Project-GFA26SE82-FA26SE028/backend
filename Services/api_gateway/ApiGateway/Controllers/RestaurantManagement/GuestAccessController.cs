using System.Globalization;
using ApiGateway.DTOs.Restaurant;
using Backend.Contracts.Restaurant;
using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Controllers.RestaurantManagement;

[ApiController]
[Route("api/table-sessions/{sessionId:guid}/guest-access")]
public sealed class GuestAccessController(GuestAccessGrpc.GuestAccessGrpcClient client) : ManagementControllerBase
{
    [HttpPost("join")]
    public Task<IActionResult> Join(Guid sessionId, JoinTableSessionDto body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.JoinAsync(new GuestJoinRequest
            {
                TableSessionId = sessionId.ToString(),
                PhoneOrCode = body.PhoneOrCode ?? ""
            }, Options(ct));
            var result = Map(reply);
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
