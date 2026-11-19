using Ordering.DAL.Extensions;
using Ordering.DAL.Models;

var builder = WebApplication.CreateBuilder(args);

Ordering.API.Extensions.OrderingServiceExtensions.AddOrderingServices(builder.Services, builder.Configuration);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Vui lòng cấu hình ConnectionStrings:DefaultConnection trong appsettings.json hoặc biến môi trường.");
}

builder.Services.AddOrderingPersistence(connectionString);

builder.Services.AddGrpcClient<Backend.Contracts.Notifications.NotificationGrpc.NotificationGrpcClient>(options =>
{
    options.Address = new Uri(builder.Configuration["GrpcServices:Identity"] ?? "http://localhost:5001");
});
builder.Services.AddHostedService<RestaurantIsolation.NotificationOutboxWorker<Ordering.DAL.Models.OrderingDbContext>>();
builder.Services.AddScoped<RestaurantIsolation.INotificationEventEnricher, Ordering.API.Clients.NotificationEventEnricher>();

builder.Services.AddScoped<Ordering.DAL.Interfaces.IKitchenAssignmentRepository, Ordering.DAL.Implements.KitchenAssignmentRepository>();
builder.Services.AddScoped<Ordering.BLL.Interfaces.IStaffTransferGuardService, Ordering.BLL.Implements.StaffTransferGuardService>();
var app = builder.Build();
app.MapGrpcService<Ordering.API.GrpcServices.StaffTransferGuardGrpcService>();

app.MapGrpcService<Ordering.API.GrpcServices.KitchenStationGrpcService>();
app.MapGrpcService<Ordering.API.GrpcServices.KitchenAssignmentGrpcService>();
app.MapGrpcService<Ordering.API.GrpcServices.CancellationRequestGrpcService>();
app.MapGrpcService<Ordering.API.GrpcServices.KitchenTaskGrpcService>();

app.MapGrpcService<Ordering.API.GrpcServices.CartGrpcService>();
app.MapGrpcService<Ordering.API.GrpcServices.OrderGrpcService>();
app.MapGrpcService<Ordering.API.GrpcServices.BillingOrderGrpcService>();
app.MapGrpcService<Ordering.API.GrpcServices.CheckoutLockGrpcService>();

app.MapGet("/health/live", () =>
{
    return Results.Ok();
});

app.MapGet("/health/ready", async (OrderingDbContext db, CancellationToken cancellationToken) =>
{
    var canConnect = await db.Database.CanConnectAsync(cancellationToken);
    return canConnect ? Results.Ok() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
});

app.Run();
