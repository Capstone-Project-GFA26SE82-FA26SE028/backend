using Identity.API.Extensions;
using Identity.BLL.Implements;
using Identity.BLL.Interfaces;
using Identity.DAL.Implements;
using Identity.DAL.Interfaces;
using Identity.DAL.UnitOfWorks;
using Identity.Domain.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

builder.Services.AddJwtAuthentication(builder.Configuration);

var databaseConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");
builder.Services.AddDbContext<IdentityDbContext>(options =>
    options.UseSqlServer(databaseConnectionString));

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IRoleRepository, RoleRepository>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.Configure<Identity.Common.Settings.UserManagementSettings>(builder.Configuration.GetSection("UserManagement"));
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<ISessionRepository, SessionRepository>();
builder.Services.AddAuthorization();

var app = builder.Build();

app.MapGrpcService<Identity.API.GrpcServices.AccessGrpcService>();

app.MapGrpcService<Identity.API.GrpcServices.IdentityGrpcService>();
app.MapGrpcService<Identity.API.GrpcServices.UserGrpcService>();
app.MapGrpcService<Identity.API.GrpcServices.RoleGrpcService>();
app.MapGrpcService<Identity.API.GrpcServices.AuditLogGrpcService>();
app.MapGrpcService<Identity.API.GrpcServices.SessionGrpcService>();

app.UseAuthentication();
app.UseAuthorization();

app.Run();
public partial class Program
{
}
