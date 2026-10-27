using Ordering.DAL.Extensions;
using Ordering.DAL.Models;

var builder = WebApplication.CreateBuilder(args);

Ordering.API.Extensions.OrderingServiceExtensions.AddOrderingServices(builder.Services, builder.Configuration);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection in appsettings.json or environment variables.");
}

builder.Services.AddOrderingPersistence(connectionString);

var app = builder.Build();

app.MapGrpcService<Ordering.API.GrpcServices.KitchenStationGrpcService>();
app.MapGrpcService<Ordering.API.GrpcServices.KitchenTaskGrpcService>();

app.MapGrpcService<Ordering.API.GrpcServices.CartGrpcService>();
app.MapGrpcService<Ordering.API.GrpcServices.OrderGrpcService>();
app.MapGrpcService<Ordering.API.GrpcServices.BillingOrderGrpcService>();

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
