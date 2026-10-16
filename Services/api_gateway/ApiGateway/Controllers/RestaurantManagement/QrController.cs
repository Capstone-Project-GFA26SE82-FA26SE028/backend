using System.Globalization;
using ApiGateway.Swagger;
using Backend.Contracts.Restaurant;
using Microsoft.AspNetCore.Mvc;
using ApiGateway.DTOs.Restaurant;

namespace ApiGateway.Controllers.RestaurantManagement;

[ApiController]
[Route("api/qr")]
public sealed class QrController(QrGrpc.QrGrpcClient client) : ManagementControllerBase
{
    [HttpGet("{id:guid}/image")]
    [RequireBearerToken]
    [ProducesResponseType(typeof(byte[]), 200, "image/png")]
    public Task<IActionResult> GetImage(Guid id, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.GetImageAsync(new IdRequest
            {
                Id = id.ToString()
            }, Options(ct));
            return File(reply.Png.ToByteArray(), "image/png");
        }, ct);
    }

    [HttpGet]
    [RequireBearerToken]
    public Task<IActionResult> GetAll([FromQuery] ListQuery query, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.GetAllAsync(ManagementMapping.ToWire(query), Options(ct));
            return new PageResult<QrDto>
            {
                Items = reply.Items.Select(Map).ToList(),
                TotalCount = reply.TotalCount,
                Page = reply.Page,
                PageSize = reply.PageSize
            };
        }, ct);
    }

    [HttpPost("tables/{tableId:guid}")]
    [RequireBearerToken]
    public Task<IActionResult> Create(Guid tableId, CancellationToken ct)
    {
        return Execute(async () =>
        {
            return Map(await client.CreateAsync(new IdRequest
            {
                Id = tableId.ToString()
            }, Options(ct)));
        }, ct, 201);
    }

    [HttpPost("tables/{tableId:guid}/regenerate")]
    [RequireBearerToken]
    public Task<IActionResult> Regenerate(Guid tableId, RegenerateQrDto body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            return Map(await client.RegenerateAsync(new QrRegenerateRequest
            {
                TableId = tableId.ToString(),
                ExpectedQrId = body.ExpectedQrId.ToString()
            }, Options(ct)));
        }, ct, 201);
    }

    [HttpPost("resolve")]
    public Task<IActionResult> Resolve(ResolveQrDto body, CancellationToken ct)
    {
        return Execute(async () =>
        {
            var reply = await client.ResolveAsync(new QrTokenRequest
            {
                Token = body.Token ?? ""
            }, Options(ct));
            return new QrTableDto
            {
                TableId = Guid.Parse(reply.TableId),
                TableCode = reply.TableCode,
                AreaId = Guid.Parse(reply.AreaId),
                RestaurantId = Guid.Parse(reply.RestaurantId)
            };
        }, ct);
    }

    private static QrCreatedDto Map(QrCreatedReply value)
    {
        return new QrCreatedDto
        {
            Qr = Map(value.Qr),
            Token = value.Token,
            MenuUrl = value.MenuUrl,
            Png = value.Png.ToByteArray()
        };
    }

    private static QrDto Map(QrInfo value)
    {
        return new QrDto
        {
            QrId = Guid.Parse(value.QrId),
            ImageUrl = value.HasImageUrl ? value.ImageUrl : null,
            TableId = Guid.Parse(value.TableId),
            TableCode = value.TableCode,
            AreaId = Guid.Parse(value.AreaId),
            CreatedAt = DateTime.Parse(value.CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            RevokedAt = value.HasRevokedAt
                ? DateTime.Parse(value.RevokedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) : null,
            IsUsable = value.IsUsable
        };
    }
}
