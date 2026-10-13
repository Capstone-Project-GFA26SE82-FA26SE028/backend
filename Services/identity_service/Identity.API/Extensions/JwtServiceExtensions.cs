using Identity.Common.Settings;
using Identity.DAL.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Identity.API.Extensions;

public static class JwtServiceExtensions
{
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtSettingModel>().Bind(configuration.GetSection("Jwt"))
            .Validate(s => !string.IsNullOrWhiteSpace(s.SecretKey) && Encoding.UTF8.GetByteCount(s.SecretKey) >= 32,
                "Set Jwt:SecretKey to a random signing key of at least 32 UTF-8 bytes.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Issuer) && !string.IsNullOrWhiteSpace(s.Audience),
                "JWT issuer and audience are required.")
            .Validate(s => s.AccessTokenMinutes > 0 && s.AccessTokenMinutes <= 60
                && s.SessionHours > 0 && s.SessionHours <= 24, "Invalid token/session lifetime.")
            .ValidateOnStart();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtSettingModel>>((options, settings) =>
            {
                var jwt = settings.Value;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    RoleClaimType = "RoleId",
                    NameClaimType = "UserName",
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SecretKey)),
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                    ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 }
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var user = context.Principal!;
                        if (!Guid.TryParse(user.FindFirst("sid")?.Value, out var sid)
                            || !Guid.TryParse(user.FindFirst("UserId")?.Value, out var uid)
                            || !await context.HttpContext.RequestServices.GetRequiredService<ISessionRepository>()
                                .IsActiveAsync(sid, uid, user.FindFirst("RoleId")?.Value ?? "", DateTime.UtcNow))
                        {
                            context.Fail("Session is no longer active.");
                        }
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = 401;
                        await context.Response.WriteAsJsonAsync(new
                        {
                            statusCode = 401,
                            message = "Token hoặc phiên không hợp lệ hoặc hết hạn.",
                            isSuccess = false
                        });
                    },
                    OnForbidden = async context =>
                    {
                        context.Response.StatusCode = 403;
                        await context.Response.WriteAsJsonAsync(new
                        {
                            statusCode = 403,
                            message = "Không có quyền truy cập chức năng này.",
                            isSuccess = false
                        });
                    }
                };
            });
        return services;
    }
}
