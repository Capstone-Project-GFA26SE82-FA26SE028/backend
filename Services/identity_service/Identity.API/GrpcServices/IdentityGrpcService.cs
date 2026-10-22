using Shared.Common.DTOs;
using Backend.Contracts.Identity;
using Grpc.Core;
using Google.Protobuf.WellKnownTypes;
using Identity.BLL.Interfaces;
using Identity.Common.DTOs;


using static Identity.Common.DTOs.AuthDTO;

namespace Identity.API.GrpcServices;

public sealed class IdentityGrpcService(IAuthService authService) : IdentityGrpc.IdentityGrpcBase
{
    public override async Task<LogoutReply> ChangePassword(ChangePasswordRequest request, ServerCallContext context)
    {
        var actor = await GrpcAuthentication.RequireUser(context);
        var response = await authService.ChangePassword(actor.UserId, new ChangePasswordDTO
        {
            CurrentPassword = request.CurrentPassword,
            NewPassword = request.NewPassword,
            ConfirmPassword = request.ConfirmPassword
        }, context.CancellationToken);

        return new LogoutReply
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? "",
            IsSuccess = response.IsSuccess
        };
    }

    public override async Task<LoginReply> Login(LoginRequest request, ServerCallContext context)
    {
        GrpcAuthentication.SetAuditContext(context);
        var response = await authService.Login(new LoginDTO
        {
            Email = request.Email,
            Password = request.Password,
            IpAddress = request.HasIpAddress ? request.IpAddress : null,
            UserAgent = request.HasUserAgent ? request.UserAgent : null
        });
        return ToReply(response);
    }

    public override async Task<LoginReply> Refresh(RefreshRequest request, ServerCallContext context)
    {
        GrpcAuthentication.SetAuditContext(context);
        var response = await authService.Refresh(new RefreshDTO { RefreshToken = request.RefreshToken });
        return ToReply(response);
    }

    public override async Task<LogoutReply> Logout(LogoutRequest request, ServerCallContext context)
    {
        var (userId, sessionId) = await GrpcAuthentication.RequireUser(context);

        var response = await authService.Logout(sessionId, userId);
        return new LogoutReply
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? string.Empty,
            IsSuccess = response.IsSuccess
        };
    }

    private static LoginReply ToReply(ResponseDTO response)
    {
        var reply = new LoginReply
        {
            StatusCode = response.StatusCode,
            Message = response.Message ?? string.Empty,
            IsSuccess = response.IsSuccess
        };
        if (response.Result is LoginResponseDTO result)
        {
            var user = result.User;
            var info = new UserInfo
            {
                UserId = user.UserId.ToString(),
                RoleId = user.RoleId,
                UserName = user.UserName,
                Email = user.Email,
                IsActive = user.IsActive,
                CreatedAt = ToTimestamp(user.CreatedAt)
            };
            if (user.PhoneNumber is not null)
            {
                info.PhoneNumber = user.PhoneNumber;
            }

            if (user.FullName is not null)
            {
                info.FullName = user.FullName;
            }

            if (user.AvatarUrl is not null)
            {
                info.AvatarUrl = user.AvatarUrl;
            }

            if (user.UpdatedAt is { } updatedAt)
            {
                info.UpdatedAt = ToTimestamp(updatedAt);
            }

            reply.Result = new LoginResult
            {
                User = info,
                AccessToken = result.AccessToken,
                RefreshToken = result.RefreshToken,
                SessionId = result.SessionId.ToString(),
                ExpiresAt = ToTimestamp(result.ExpiresAt),
                SessionExpiresAt = ToTimestamp(result.SessionExpiresAt)
            };
        }
        return reply;
    }

    // SQL datetime values are stored as UTC but do not retain DateTime.Kind.
    private static Timestamp ToTimestamp(DateTime value)
    {
        return Timestamp.FromDateTime(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    public override Task<PingReply> Ping(
        PingRequest request,
        ServerCallContext context)
    {
        return Task.FromResult(new PingReply
        {
            Message = $"Identity received: {request.Name}"
        });
    }
}
