using Backend.Contracts.Catalog;
using Catalog.BLL.Interfaces;
using Grpc.Core;
using Shared.Common.Images;

namespace Catalog.API.GrpcServices;

public sealed class ItemImageGrpcService(IItemImageService service) : ItemImageGrpc.ItemImageGrpcBase
{
    public override async Task<ItemImageUploadReply> Upload(ItemImageUploadRequest request, ServerCallContext context)
    {
        try
        {
            return new ItemImageUploadReply
            {
                ImageUrl = await service.Upload(request.Image.ToByteArray(), context.CancellationToken)
            };
        }
        catch (ImageStorageException ex)
        {
            throw new RpcException(new Status(ex.StatusCode == 400 ? StatusCode.InvalidArgument : StatusCode.Unavailable,
                ex.Message));
        }
    }
}
