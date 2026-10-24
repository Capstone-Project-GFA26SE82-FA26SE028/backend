using Customer.API.GrpcServices;
using Customer.BLL.Extensions;
using Customer.DAL.Extensions;
using Customer.DAL.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection in appsettings.json or environment variables.");
}

builder.Services.AddCustomerPersistence(connectionString);
builder.Services.AddCustomerBusinessServices();

var app = builder.Build();

app.MapGrpcService<CustomerGrpcService>();
app.MapGrpcService<FeedbackGrpcService>();

app.MapGet("/health/live", () =>
{
    return Results.Ok();
});

app.MapGet("/health/ready", async (CustomerDbContext db, CancellationToken cancellationToken) =>
{
    var canConnect = await db.Database.CanConnectAsync(cancellationToken);
    return canConnect ? Results.Ok() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
});

app.Run();
