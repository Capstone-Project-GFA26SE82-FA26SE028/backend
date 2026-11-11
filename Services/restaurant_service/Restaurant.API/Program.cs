using Restaurant.DAL.Extensions;
using Restaurant.DAL.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<Restaurant.API.Management.ManagementInterceptor>();
});
builder.Services.AddHttpContextAccessor();
builder.Services.Configure<Restaurant.Common.Settings.BillingReadSettings>(builder.Configuration.GetSection("BillingRead"));
builder.Services.AddScoped<Restaurant.BLL.Interfaces.IBillingSessionService, Restaurant.BLL.Implements.BillingSessionService>();
builder.Services.AddOptions<Restaurant.Common.Settings.ServiceRequestSettings>()
    .Bind(builder.Configuration.GetSection("ServiceRequests"))
    .Validate(value => value.WaiterRoleIds.Length > 0 && value.CashierRoleIds.Length > 0
        && value.DependencyTimeoutSeconds is > 0 and <= 30
        && value.WaiterRoleIds.Concat(value.CashierRoleIds).All(role => !string.IsNullOrWhiteSpace(role))
        && !value.WaiterRoleIds.Intersect(value.CashierRoleIds, StringComparer.Ordinal).Any(),
        "Vui lòng cấu hình vai trò phục vụ và thu ngân riêng biệt cho yêu cầu thanh toán.")
    .ValidateOnStart();
builder.Services.AddScoped<Restaurant.BLL.Interfaces.IServiceRequestService, Restaurant.BLL.Implements.ServiceRequestService>();
builder.Services.AddScoped<Restaurant.BLL.Interfaces.IPaymentRequestOrderClient, Restaurant.API.Management.PaymentRequestOrderClient>();
builder.Services.AddScoped<Restaurant.BLL.Interfaces.ISessionPaymentClient, Restaurant.API.Management.SessionPaymentClient>();
builder.Services.AddGrpcClient<Backend.Contracts.Billing.PaymentGrpc.PaymentGrpcClient>(options =>
{
    options.Address = new Uri(builder.Configuration["GrpcServices:Billing"]
        ?? throw new InvalidOperationException("Vui lòng cấu hình GrpcServices:Billing."));
});
builder.Services.AddGrpcClient<Backend.Contracts.Ordering.CheckoutLockGrpc.CheckoutLockGrpcClient>(options =>
{
    options.Address = new Uri(builder.Configuration["GrpcServices:Ordering"]
        ?? throw new InvalidOperationException("Vui lòng cấu hình GrpcServices:Ordering."));
});
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.Configure<Restaurant.Common.Settings.ReservationSettings>(builder.Configuration.GetSection("Reservations"));
builder.Services.Configure<Restaurant.Common.Settings.RestaurantStaffSettings>(builder.Configuration.GetSection("RestaurantStaff"));
builder.Services.AddScoped<Restaurant.BLL.Interfaces.IStaffIdentity, Restaurant.API.Management.IdentityStaffAccess>();
builder.Services.AddScoped<Restaurant.BLL.Interfaces.IReservationAccess, Restaurant.BLL.Implements.ReservationAccess>();
builder.Services.AddScoped<Restaurant.BLL.Interfaces.IReservationService, Restaurant.BLL.Implements.ReservationService>();
builder.Services.AddScoped<Restaurant.BLL.Interfaces.IReservationExpirationService, Restaurant.BLL.Implements.ReservationExpirationService>();
builder.Services.AddHostedService<Restaurant.API.Reservations.ReservationExpirationWorker>();
builder.Services.AddScoped<Restaurant.BLL.Interfaces.ITableSessionOrderAccess, Restaurant.API.Management.IdentityTableSessionOrderAccess>();
builder.Services.AddScoped<Restaurant.BLL.Interfaces.IGuestAccessService, Restaurant.BLL.Implements.GuestAccessService>();
builder.Services.AddOptions<Restaurant.Common.Settings.GuestAccessSettings>()
    .Bind(builder.Configuration.GetSection("GuestAccess"))
    .Validate(x => double.IsFinite(x.LifetimeHours) && x.LifetimeHours > 0 && x.LifetimeHours <= 8760,
        "GuestAccess:LifetimeHours phải lớn hơn 0 và không vượt quá 8760.")
    .ValidateOnStart();
builder.Services.AddScoped<Restaurant.BLL.Interfaces.IManagementAccess, Restaurant.API.Management.IdentityManagementAccess>();
builder.Services.AddScoped<Restaurant.BLL.Interfaces.IRestaurantService, Restaurant.BLL.Implements.RestaurantService>();
builder.Services.AddScoped<Restaurant.BLL.Interfaces.IAreaService, Restaurant.BLL.Implements.AreaService>();
builder.Services.AddScoped<Restaurant.BLL.Interfaces.ITableService, Restaurant.BLL.Implements.TableService>();
builder.Services.AddScoped<Restaurant.BLL.Interfaces.IQrService, Restaurant.BLL.Implements.QrService>();
builder.Services.AddScoped<Restaurant.BLL.Interfaces.ITableSessionService, Restaurant.BLL.Implements.TableSessionService>();
builder.Services.Configure<Restaurant.Common.Settings.QrSettings>(builder.Configuration.GetSection("Qr"));
builder.Services.AddSingleton(builder.Configuration.GetSection("Cloudinary")
    .Get<Shared.Common.Images.CloudinarySettings>() ?? new Shared.Common.Images.CloudinarySettings());
builder.Services.AddHttpClient<Shared.Common.Images.IImageStorage, Shared.Common.Images.CloudinaryImageStorage>()
    .ConfigurePrimaryHttpMessageHandler(() =>
{
    return new HttpClientHandler
    {
        AllowAutoRedirect = false
    };
});
builder.Services.AddScoped<Restaurant.BLL.Interfaces.IQrImageStore, Restaurant.BLL.Implements.QrImageStore>();
builder.Services.AddGrpcClient<Backend.Contracts.Identity.AccessGrpc.AccessGrpcClient>(options =>
{
    options.Address = new Uri(builder.Configuration["GrpcServices:Identity"]
        ?? throw new InvalidOperationException("Vui lòng cấu hình GrpcServices:Identity."));
});

builder.Services.AddGrpcClient<Backend.Contracts.Identity.UserGrpc.UserGrpcClient>(options =>
{
    options.Address = new Uri(builder.Configuration["GrpcServices:Identity"]
        ?? throw new InvalidOperationException("Vui lòng cấu hình GrpcServices:Identity."));
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Vui lòng cấu hình ConnectionStrings:DefaultConnection trong appsettings.json hoặc biến môi trường.");
}

builder.Services.AddRestaurantPersistence(connectionString);

builder.Services.AddScoped<Restaurant.BLL.Interfaces.IRestaurantStaffService, Restaurant.BLL.Implements.RestaurantStaffService>();
builder.Services.AddScoped<Restaurant.BLL.Interfaces.ICurrentRestaurantScopeService, Restaurant.BLL.Implements.CurrentRestaurantScopeService>();

var app = builder.Build();

app.MapGrpcService<Restaurant.API.GrpcServices.RestaurantGrpcService>();
app.MapGrpcService<Restaurant.API.GrpcServices.AreaGrpcService>();
app.MapGrpcService<Restaurant.API.GrpcServices.TableGrpcService>();
app.MapGrpcService<Restaurant.API.GrpcServices.QrGrpcService>();
app.MapGrpcService<Restaurant.API.GrpcServices.TableSessionGrpcService>();
app.MapGrpcService<Restaurant.API.GrpcServices.ReservationGrpcService>();
app.MapGrpcService<Restaurant.API.GrpcServices.RestaurantStaffGrpcService>();
app.MapGrpcService<Restaurant.API.GrpcServices.RestaurantAccessGrpcService>();
app.MapGrpcService<Restaurant.API.GrpcServices.BillingSessionGrpcService>();
app.MapGrpcService<Restaurant.API.GrpcServices.ServiceRequestGrpcService>();
app.MapGrpcService<Restaurant.API.GrpcServices.GuestAccessGrpcService>();

app.MapGet("/health/live", () =>
{
    return Results.Ok();
});

app.MapGet("/health/ready", async (RestaurantDbContext db, CancellationToken cancellationToken) =>
{
    var canConnect = await db.Database.CanConnectAsync(cancellationToken);
    return canConnect ? Results.Ok() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
});

app.Run();
