using Backend.Contracts.Identity;
using Grpc.Core;

namespace Identity.API.GrpcServices;

public sealed partial class UserGrpcService
{
    public override async Task<UserReply> UploadAvatar(UploadAvatarRequest request, ServerCallContext context)
    {
        var actor = await GrpcAuthentication.RequireUser(context);
        var userId = request.HasUserId ? ParseId(request.UserId) : actor.UserId;
        return Map(await users.UploadAvatar(actor.UserId, userId, request.Image.ToByteArray(), context.CancellationToken));
    }
}
