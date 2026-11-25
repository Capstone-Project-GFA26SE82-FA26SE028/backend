using System.Globalization;
using Backend.Contracts.Restaurant;
using Billing.API.Settings;
using Billing.BLL.Interfaces;
using Billing.Common.DTOs;
using Billing.Common.Exceptions;
using Grpc.Core;
using Microsoft.Extensions.Options;

namespace Billing.API.Clients;

public sealed class BillingSessionDisplayClient(BillingSessionGrpc.BillingSessionGrpcClient client,
    IHttpContextAccessor http, IOptions<RestaurantAccessSettings> settings) : IBillingSessionDisplayClient
{
    public async Task<IReadOnlyDictionary<Guid, BillingSessionDisplayDTO>> GetByIdsAsync(Guid restaurantId,
        IReadOnlyList<Guid> sessionIds, CancellationToken ct)
    {
        var result = new Dictionary<Guid, BillingSessionDisplayDTO>();
        if (sessionIds.Count == 0)
        {
            return result;
        }

        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            throw new BillingException(401, "Yêu cầu phiên đăng nhập nhân viên hợp lệ.");
        }

        var deadline = DateTime.UtcNow.AddSeconds(settings.Value.TimeoutSeconds);
        foreach (var batch in sessionIds.Distinct().Chunk(200))
        {
            var request = new BillingSessionDisplayRequest
            {
                RestaurantId = restaurantId.ToString()
            };
            request.TableSessionIds.AddRange(batch.Select(id => id.ToString()));
            var reply = await client.GetDisplaysAsync(request, new Metadata
            {
                { "authorization", authorization },
                { "x-restaurant-id", restaurantId.ToString() }
            }, deadline: deadline, cancellationToken: ct);
            foreach (var item in reply.Items)
            {
                if (!Guid.TryParse(item.TableSessionId, out var sessionId) || !batch.Contains(sessionId)
                    || !Guid.TryParse(item.RestaurantId, out var owner) || owner != restaurantId
                    || item.Tables.Count == 0 || item.PartySize < 0
                    || !DateTimeOffset.TryParse(item.OpenedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var openedAt)
                    || item.Tables.Any(table => !Guid.TryParse(table.TableId, out var tableId) || tableId == Guid.Empty
                        || !Guid.TryParse(table.AreaId, out var areaId) || areaId == Guid.Empty))
                {
                    throw new BillingException(503, "Nhà hàng trả về dữ liệu bàn không hợp lệ.");
                }

                if (!result.TryAdd(sessionId, new BillingSessionDisplayDTO(sessionId, owner, item.PartySize, openedAt,
                    item.Tables.Select(table => new BillingTableDisplayDTO(Guid.Parse(table.TableId), table.TableCode,
                        Guid.Parse(table.AreaId), table.AreaName)).ToArray())))
                {
                    throw new BillingException(503, "Nhà hàng trả về phiên bàn trùng lặp.");
                }
            }

            if (batch.Any(id => !result.ContainsKey(id)))
            {
                throw new BillingException(503, "Không lấy được đầy đủ thông tin bàn của các hóa đơn.");
            }
        }

        return result;
    }
}
