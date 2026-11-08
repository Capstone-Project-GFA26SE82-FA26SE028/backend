using Grpc.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Identity.API.GrpcServices;

internal static class GrpcAuthentication
{
    public static void SetAuditContext(ServerCallContext context)
    {
        // Only the trusted gateway should be able to reach the internal gRPC service.
        var forwarded = context.RequestHeaders.FirstOrDefault(header => header.Key == "x-client-ip")?.Value;
        var http = context.GetHttpContext();
        var ip = System.Net.IPAddress.TryParse(forwarded, out var address)
            ? address : http.Connection.RemoteIpAddress;
        http.RequestServices.GetRequiredService<Identity.Domain.Models.IdentityDbContext>()
            .AuditIpAddress = ip?.ToString();
    }

    public static async Task<(Guid UserId, Guid SessionId)> RequireUser(ServerCallContext context)
    {
        SetAuditContext(context);
        var authentication = await context.GetHttpContext().AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        var user = authentication.Principal;
        if (!authentication.Succeeded || user is null
            || !Guid.TryParse(user.FindFirst("UserId")?.Value, out var userId)
            || !Guid.TryParse(user.FindFirst("sid")?.Value, out var sessionId))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Phiên đăng nhập không hợp lệ hoặc đã hết hạn."));
        }

        return (userId, sessionId);
    }
}
