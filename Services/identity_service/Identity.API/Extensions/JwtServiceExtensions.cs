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
                "Đặt Jwt:SecretKey thành khóa ký ngẫu nhiên có độ dài ít nhất 32 byte UTF-8.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.Issuer) && !string.IsNullOrWhiteSpace(s.Audience),
                "Yêu cầu cấu hình bên phát hành và đối tượng nhận JWT.")
            .Validate(s => s.AccessTokenMinutes > 0 && s.AccessTokenMinutes <= 60
                && s.SessionHours > 0 && s.SessionHours <= 24, "Thời hạn mã xác thực hoặc phiên đăng nhập không hợp lệ.")
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
                            context.Fail("Phiên đăng nhập không còn hoạt động.");
                        }
                    },
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        context.Response.StatusCode = 401;
                        await context.Response.WriteAsJsonAsync(new
                        {
                            statusCode = 401,
                            message = "Mã xác thực hoặc phiên đăng nhập không hợp lệ hoặc đã hết hạn.",
                            isSuccess = false
                        });
                    },
                    OnForbidden = async context =>
                    {
                        context.Response.StatusCode = 403;
                        await context.Response.WriteAsJsonAsync(new
                        {
                            statusCode = 403,
                            message = "Bạn không có quyền truy cập chức năng này.",
                            isSuccess = false
                        });
                    }
                };
            });
        return services;
    }
}
