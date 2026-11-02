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
                return new ResponseDTO("Phản hồi trạm bếp không hợp lệ.", 503);
            }
            return station.IsActive ? null : new ResponseDTO("Trạm bếp đang ngừng hoạt động.", 409);
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
            return new ResponseDTO(code == 404 ? "Không tìm thấy trạm bếp."
                : "Không thể xác minh trạm bếp. Vui lòng kiểm tra lại dịch vụ Ordering.", code);
        }
    }
}
