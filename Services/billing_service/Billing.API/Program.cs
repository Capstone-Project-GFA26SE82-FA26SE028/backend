using Billing.DAL.Extensions;
using Billing.DAL.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection in appsettings.json or environment variables.");
}

builder.Services.AddBillingPersistence(connectionString);

var app = builder.Build();

app.MapGet("/health/live", () =>
{
    return Results.Ok();
});

app.MapGet("/health/ready", async (BillingDbContext db, CancellationToken cancellationToken) =>
{
    var canConnect = await db.Database.CanConnectAsync(cancellationToken);
    return canConnect ? Results.Ok() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
});

app.Run();
