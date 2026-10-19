using Catalog.DAL.Extensions;
using Catalog.DAL.Models;
using Catalog.API.Extensions;
using Catalog.API.GrpcServices;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddCatalogManagement(builder.Configuration);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection in appsettings.json or environment variables.");
}

builder.Services.AddCatalogPersistence(connectionString);

var app = builder.Build();

app.MapGrpcService<CategoryGrpcService>();
app.MapGrpcService<ItemGrpcService>();
app.MapGrpcService<PriceGrpcService>();

app.MapGet("/health/live", () =>
{
    return Results.Ok();
});

app.MapGet("/health/ready", async (CatalogDbContext db, CancellationToken cancellationToken) =>
{
    var canConnect = await db.Database.CanConnectAsync(cancellationToken);
    return canConnect ? Results.Ok() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
});

app.Run();
