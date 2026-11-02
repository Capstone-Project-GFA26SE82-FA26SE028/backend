using Shared.Common.DTOs;
using ApiGateway.DTOs.Identity;
using ApiGateway.DTOs;
using Backend.Contracts.Identity;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;

namespace ApiGateway.Controllers.IdentityController;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IdentityGrpc.IdentityGrpcClient client) : ControllerBase
{
    [HttpPost("login")]
    public Task<IActionResult> Login(LoginDTO body, CancellationToken cancellationToken)
    {
        return ExecuteAuth(async () => await client.LoginAsync(new LoginRequest
        {
            Email = body.Email ?? string.Empty,
            Password = body.Password ?? string.Empty,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            UserAgent = Request.Headers.UserAgent.ToString()
        }, ClientHeaders(), deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: cancellationToken), cancellationToken);
    }

    [HttpPost("refresh")]
    public Task<IActionResult> Refresh(RefreshDTO body, CancellationToken cancellationToken)
    {
        return ExecuteAuth(async () => await client.RefreshAsync(new RefreshRequest
        {
            RefreshToken = body.RefreshToken ?? string.Empty
        }, ClientHeaders(), deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: cancellationToken), cancellationToken);
    }

    [ApiGateway.Swagger.RequireBearerToken]
    [HttpPost("logout")]
    public Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        return ExecuteAuth(async () =>
        {
            var headers = ClientHeaders();
            var authorization = Request.Headers.Authorization.ToString();
            if (!string.IsNullOrWhiteSpace(authorization))
            {
                headers.Add("authorization", authorization);
            }

            var reply = await client.LogoutAsync(new LogoutRequest(), headers,
                deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: cancellationToken);
            return new LoginReply
            {
                StatusCode = reply.StatusCode,
                Message = reply.Message,
                IsSuccess = reply.IsSuccess
            };
        }, cancellationToken);
    }

    private Metadata ClientHeaders()
    {
        var headers = new Metadata();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (ip is not null)
        {
            headers.Add("x-client-ip", ip);
        }

        return headers;
    }

    [ApiGateway.Swagger.RequireBearerToken]
    [HttpPost("change-password")]
    public Task<IActionResult> ChangePassword(ChangePasswordDTO body, CancellationToken ct)
    {
        return ExecuteAuth(async () =>
        {
            var headers = ClientHeaders();
            var authorization = Request.Headers.Authorization.ToString();
            if (!string.IsNullOrWhiteSpace(authorization))
            {
                headers.Add("authorization", authorization);
            }

            var reply = await client.ChangePasswordAsync(new ChangePasswordRequest
            {
                CurrentPassword = body.CurrentPassword,
                NewPassword = body.NewPassword,
                ConfirmPassword = body.ConfirmPassword
            }, headers, deadline: DateTime.UtcNow.AddSeconds(10), cancellationToken: ct);

            return new LoginReply
            {
                StatusCode = reply.StatusCode,
                Message = reply.Message,
                IsSuccess = reply.IsSuccess
            };
        }, ct);
    }

    private async Task<IActionResult> ExecuteAuth(Func<Task<LoginReply>> call, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var reply = await call();

            var result = reply.Result;
            var user = result?.User;
            return StatusCode(reply.StatusCode, new ResponseDTO
            {
                StatusCode = reply.StatusCode,
                Message = reply.Message,
                IsSuccess = reply.IsSuccess,
                Result = result is null ? null : new LoginResponseDTO
                {
                    User = user is null ? null : new UserDTO
                    {
                        UserId = user.UserId,
                        RoleId = user.RoleId,
                        UserName = user.UserName,
                        Email = user.Email,
                        PhoneNumber = user.HasPhoneNumber ? user.PhoneNumber : null,
                        FullName = user.HasFullName ? user.FullName : null,
                        AvatarUrl = user.HasAvatarUrl ? user.AvatarUrl : null,
                        IsActive = user.IsActive,
                        CreatedAt = user.CreatedAt?.ToDateTime(),
                        UpdatedAt = user.UpdatedAt?.ToDateTime()
                    },
                    AccessToken = result.AccessToken,
                    RefreshToken = result.RefreshToken,
                    SessionId = result.SessionId,
                    ExpiresAt = result.ExpiresAt?.ToDateTime(),
                    SessionExpiresAt = result.SessionExpiresAt?.ToDateTime()
                }
            });
        }
        catch (RpcException ex) when (!cancellationToken.IsCancellationRequested)
        {
            var (status, message) = ex.StatusCode switch
            {
                Grpc.Core.StatusCode.Unavailable => (503, "Hệ thống hiện đang bận hoặc tạm thời không khả dụng, vui lòng thử lại sau."),
                Grpc.Core.StatusCode.DeadlineExceeded => (504, "Hết thời gian chờ phản hồi từ hệ thống, vui lòng thử lại sau."),
                Grpc.Core.StatusCode.InvalidArgument => (400, "Thông tin đăng nhập không hợp lệ."),
                Grpc.Core.StatusCode.Unauthenticated => (401, "Tài khoản hoặc mật khẩu không chính xác."),
                Grpc.Core.StatusCode.PermissionDenied => (403, "Truy cập bị từ chối."),
                _ => (502, "Không thể xử lý yêu cầu lúc này, vui lòng thử lại sau.")
            };
            return StatusCode(status, new ResponseDTO { StatusCode = status, Message = message, IsSuccess = false });
        }
    }




}
