using Backend.Contracts.Ordering;
using Catalog.BLL.Interfaces;
using Grpc.Core;
using Shared.Common.DTOs;

namespace Catalog.API.Authorization;

public sealed class OrderingKitchenStationLookup(KitchenStationGrpc.KitchenStationGrpcClient client,
    IHttpContextAccessor http) : IKitchenStationLookup
{
    public async Task<ResponseDTO?> ValidateAsync(Guid stationId, CancellationToken ct)
    {
        var headers = new Metadata();
        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(authorization))
        {
            headers.Add("authorization", authorization);
        }
        try
        {
            var station = await client.GetByIdAsync(new KitchenStationIdRequest
            {
                Id = stationId.ToString()
            }, headers, deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: ct);
            if (!Guid.TryParse(station.Id, out var returnedId) || returnedId != stationId)
            {
                return new ResponseDTO("Invalid kitchen station response.", 503);
            }
            return station.IsActive ? null : new ResponseDTO("Kitchen station is inactive.", 409);
        }
        catch (RpcException error) when (!ct.IsCancellationRequested)
        {
            var code = error.StatusCode switch
            {
                StatusCode.NotFound => 404,
                StatusCode.Unauthenticated => 401,
                StatusCode.PermissionDenied => 403,
                _ => 503
            };
            return new ResponseDTO(code == 404 ? "Kitchen station not found."
                : "Unable to verify kitchen station. Retry after checking Ordering.", code);
        }
    }
}
