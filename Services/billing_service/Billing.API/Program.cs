using Billing.DAL.Extensions;
using Billing.DAL.Models;
using Billing.API.Extensions;
using Billing.API.GrpcServices;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables().AddCommandLine(args);

builder.Services.AddBillingServices(builder.Configuration);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection in appsettings.json or environment variables.");
}

builder.Services.AddBillingPersistence(connectionString);

var app = builder.Build();

app.MapGrpcService<CashRegisterGrpcService>();
app.MapGrpcService<CashShiftGrpcService>();
app.MapGrpcService<BillGrpcService>();
app.MapGrpcService<ShiftReportGrpcService>();
app.MapGrpcService<PaymentGrpcService>();
app.MapGrpcService<PayOsPaymentGrpcService>();
app.MapGrpcService<PaymentWebhookGrpcService>();
app.MapGrpcService<CashMovementGrpcService>();
app.MapGrpcService<BillCheckoutGrpcService>();
app.MapGrpcService<TaxFeePolicyGrpcService>();

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
