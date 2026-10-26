using Backend.Contracts.Identity;
using Grpc.Core;
using Inventory.BLL.Interfaces;
using Inventory.Common;

namespace Inventory.API.Clients;

public sealed class IdentityInventoryAccess(UserGrpc.UserGrpcClient client, IHttpContextAccessor http,
    IConfiguration configuration) : IInventoryAccess
{
    public async Task<Guid> RequireAsync(string operation, CancellationToken ct)
    {
        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            throw new InventoryException(401, "Authentication is required.");
        }
        var headers = new Metadata
        {
            { "authorization", authorization }
        };
        var reply = await client.GetMeAsync(new GetMeRequest(), headers,
            deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: ct);
        if (!reply.IsSuccess)
        {
            throw new InventoryException(reply.StatusCode is 401 or 404 ? 401
                : reply.StatusCode == 403 ? 403 : 503, "Unable to verify the account.");
        }
        if (reply.Result is null || !reply.Result.IsActive
            || !Guid.TryParse(reply.Result.UserId, out var id) || id == Guid.Empty)
        {
            throw new InventoryException(401, "Invalid or inactive account.");
        }
        var section = operation switch
        {
            "ManageIngredients" => "ManagerRoleIds",
            "Receive" => "ReceiverRoleIds",
            "Issue" => "IssuerRoleIds",
            "Read" => "ReaderRoleIds",
            "Cooking" => "CookingRoleIds",
            _ => throw new InventoryException(403, "Unknown inventory permission.")
        };
        var configured = configuration.GetSection($"InventoryAccess:{section}").Get<string[]>();
        var allowed = configured is not null && configured.Length > 0
            ? configured
            : operation == "Cooking"
                ? ["kitchen", "manager", "admin"]
                : [];
        if (!allowed.Contains(reply.Result.RoleId, StringComparer.Ordinal))
        {
            throw new InventoryException(403, "You do not have permission for this inventory operation.");
        }
        return id;
    }
}
