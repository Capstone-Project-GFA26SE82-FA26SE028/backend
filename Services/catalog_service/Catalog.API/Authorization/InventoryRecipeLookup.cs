using System.Globalization;
using Backend.Contracts.Inventory;
using Catalog.BLL.Interfaces;
using Grpc.Core;
using Shared.Common.DTOs;

namespace Catalog.API.Authorization;

public sealed class InventoryRecipeLookup(IngredientUnitGrpc.IngredientUnitGrpcClient client,
    IHttpContextAccessor http) : IInventoryRecipeLookup
{
    public async Task<(decimal Factor, ResponseDTO? Error)> ResolveAsync(Guid ingredientId, Guid unitId, CancellationToken ct)
    {
        var headers = new Metadata();
        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(authorization))
        {
            headers.Add("authorization", authorization);
        }
        try
        {
            var reply = await client.ResolveAsync(new ConversionRequest
            {
                IngredientId = ingredientId.ToString(),
                UnitId = unitId.ToString()
            }, headers, deadline: DateTime.UtcNow.AddSeconds(5), cancellationToken: ct);
            if (!Guid.TryParse(reply.IngredientId, out var actualIngredient) || actualIngredient != ingredientId
                || !Guid.TryParse(reply.UnitId, out var actualUnit) || actualUnit != unitId
                || !decimal.TryParse(reply.Factor, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var factor)
                || factor <= 0 || factor > 999999999999.999999m || decimal.Round(factor, 6) != factor)
            {
                return (0, new ResponseDTO("Hệ thống hiện đang bận hoặc tạm thời không khả dụng, vui lòng thử lại sau.", 503));
            }
            return (factor, null);
        }
        catch (RpcException exception) when (!ct.IsCancellationRequested)
        {
            var status = exception.StatusCode switch
            {
                StatusCode.InvalidArgument => 400,
                StatusCode.NotFound => 404,
                StatusCode.Unauthenticated => 401,
                StatusCode.PermissionDenied => 403,
                StatusCode.Aborted or StatusCode.FailedPrecondition => 409,
                StatusCode.DeadlineExceeded => 504,
                _ => 503
            };
            return (0, new ResponseDTO(status >= 500 ? "Hệ thống hiện đang bận hoặc tạm thời không khả dụng, công thức chưa được lưu." : exception.Status.Detail, status));
        }
    }
}
