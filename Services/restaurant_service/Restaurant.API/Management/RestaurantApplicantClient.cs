using Backend.Contracts.Identity;
using Grpc.Core;
using Restaurant.BLL.Interfaces;
using Restaurant.Common.Exceptions;

namespace Restaurant.API.Management;

public sealed class RestaurantApplicantClient(RestaurantApplicantGrpc.RestaurantApplicantGrpcClient client,
    IConfiguration config, IHttpContextAccessor http) : IRestaurantApplicantClient
{
    public async Task<RestaurantApplicant> Reserve(Guid registrationId, RestaurantApplicantCredentials credentials, CancellationToken ct)
    {
        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        var reply = await client.ReserveAsync(new RestaurantApplicantReserveRequest
        {
            RegistrationId = registrationId.ToString(),
            Email = credentials.Email,
            Password = credentials.Password,
            FullName = credentials.FullName,
            PhoneNumber = credentials.PhoneNumber
        }, Options(authorization, ct));
        if (!Guid.TryParse(reply.UserId, out var userId) || userId == Guid.Empty || string.IsNullOrWhiteSpace(reply.Email))
        {
            throw new ManagementException(503, "Không thể xác minh người nộp hồ sơ.");
        }

        return new RestaurantApplicant(userId, reply.Email, string.IsNullOrWhiteSpace(authorization));
    }

    public async Task Activate(Guid registrationId, Guid userId, Guid restaurantId, bool newAccount, CancellationToken ct)
    {
        var authorization = http.HttpContext?.Request.Headers.Authorization.ToString();
        await client.ActivateAsync(new RestaurantApplicantActivateRequest
        {
            RegistrationId = registrationId.ToString(),
            UserId = userId.ToString(),
            RestaurantId = restaurantId.ToString(),
            NewAccount = newAccount
        }, Options(authorization, ct));
    }

    private CallOptions Options(string? authorization, CancellationToken ct)
    {
        var key = config["StaffProvisioning:ServiceKey"];
        if (key is null || key.Length < 32)
        {
            throw new ManagementException(503, "Chưa cấu hình dịch vụ cấp tài khoản nhà hàng.");
        }

        var headers = new Metadata
        {
            { "x-staff-provisioning-key", key }
        };
        if (!string.IsNullOrWhiteSpace(authorization))
        {
            headers.Add("authorization", authorization);
        }

        return new CallOptions(headers, DateTime.UtcNow.AddSeconds(10), ct);
    }
}
