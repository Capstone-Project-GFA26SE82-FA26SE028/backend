using Backend.Contracts.Restaurant;
using Google.Protobuf;
using Grpc.Core;
using Restaurant.API.Management;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.DTOs;

namespace Restaurant.API.GrpcServices;

public sealed class QrGrpcService(IQrService service) : QrGrpc.QrGrpcBase
{
    public override async Task<QrImageReply> GetImage(IdRequest request, ServerCallContext context)
    {
        return new QrImageReply
        {
            Png = ByteString.CopyFrom(await service.GetImage(ManagementMapping.Id(request.Id), context.CancellationToken))
        };
    }

    public override async Task<QrPage> GetAll(ListRequest request, ServerCallContext context)
    {
        var result = await service.GetAll(ManagementMapping.ToDto(request), context.CancellationToken);
        var reply = new QrPage
        {
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize
        };
        reply.Items.AddRange(result.Items.Select(Map));
        return reply;
    }

    public override async Task<QrCreatedReply> Create(IdRequest request, ServerCallContext context)
    {
        return Map(await service.Create(ManagementMapping.Id(request.Id), context.CancellationToken));
    }

    public override async Task<QrCreatedReply> Regenerate(QrRegenerateRequest request, ServerCallContext context)
    {
        return Map(await service.Regenerate(ManagementMapping.Id(request.TableId),
            ManagementMapping.Id(request.ExpectedQrId), context.CancellationToken));
    }

    public override async Task<QrTableReply> Resolve(QrTokenRequest request, ServerCallContext context)
    {
        var result = await service.Resolve(request.Token, context.CancellationToken);
        return new QrTableReply
        {
            TableId = result.TableId.ToString(),
            TableCode = result.TableCode,
            AreaId = result.AreaId.ToString(),
            RestaurantId = result.RestaurantId.ToString()
        };
    }

    private static QrCreatedReply Map(QrCreatedDto value)
    {
        return new QrCreatedReply
        {
            Qr = Map(value.Qr),
            MenuUrl = value.MenuUrl,
            Token = value.Token,
            Png = ByteString.CopyFrom(value.Png)
        };
    }

    private static QrInfo Map(QrDto value)
    {
        var result = new QrInfo
        {
            QrId = value.QrId.ToString(),
            TableId = value.TableId.ToString(),
            TableCode = value.TableCode,
            AreaId = value.AreaId.ToString(),
            CreatedAt = value.CreatedAt.ToString("O"),
            IsUsable = value.IsUsable
        };
        if (value.ImageUrl is not null)
        {
            result.ImageUrl = value.ImageUrl;
        }

        if (value.RevokedAt.HasValue)
        {
            result.RevokedAt = value.RevokedAt.Value.ToString("O");
        }

        return result;
    }
}
