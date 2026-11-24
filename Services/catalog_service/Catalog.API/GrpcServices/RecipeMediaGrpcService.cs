using Backend.Contracts.Catalog;
using Catalog.BLL.Interfaces;
using Grpc.Core;
using Shared.Common.Images;

namespace Catalog.API.GrpcServices;

public sealed class RecipeMediaGrpcService(IRecipeMediaService media) : RecipeMediaGrpc.RecipeMediaGrpcBase
{
    public override async Task<RecipeMediaUploadReply> Upload(RecipeMediaUploadRequest request, ServerCallContext context)
    {
        try
        {
            var result = await media.Upload(request.File.ToByteArray(), context.CancellationToken);
            return new RecipeMediaUploadReply
            {
                MediaType = result.MediaType,
                Url = result.Url
            };
        }
        catch (ImageStorageException error)
        {
            throw new RpcException(new Status(error.StatusCode == 400 ? StatusCode.InvalidArgument : StatusCode.Unavailable,
                error.Message));
        }
    }
}
