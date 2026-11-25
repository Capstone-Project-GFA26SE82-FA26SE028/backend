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
    throw new InvalidOperationException("Vui lòng cấu hình ConnectionStrings:DefaultConnection trong appsettings.json hoặc biến môi trường.");
}

builder.Services.AddBillingPersistence(connectionString);

builder.Services.AddGrpcClient<Backend.Contracts.Notifications.NotificationGrpc.NotificationGrpcClient>(options =>
{
    options.Address = new Uri(builder.Configuration["GrpcServices:Identity"] ?? "http://localhost:5001");
});
builder.Services.AddHostedService<RestaurantIsolation.NotificationOutboxWorker<Billing.DAL.Models.BillingDbContext>>();

var app = builder.Build();
app.MapGrpcService<Billing.API.GrpcServices.TableTransferBillingGrpcService>();

app.MapGrpcService<CashRegisterGrpcService>();
app.MapGrpcService<CashShiftGrpcService>();
app.MapGrpcService<BillGrpcService>();
app.MapGrpcService<ShiftReportGrpcService>();
app.MapGrpcService<PaymentGrpcService>();
app.MapGrpcService<PaymentSettlementGrpcService>();
app.MapGrpcService<CashHandoverGrpcService>();
app.MapGrpcService<PayOsPaymentGrpcService>();
app.MapGrpcService<OnlinePaymentGrpcService>();
app.MapGrpcService<PaymentProviderConfigGrpcService>();
app.MapGrpcService<PaymentWebhookGrpcService>();
app.MapGrpcService<CashMovementGrpcService>();
app.MapGrpcService<BillCheckoutGrpcService>();
app.MapGrpcService<PromotionGrpcService>();
app.MapGrpcService<VoucherGrpcService>();
app.MapGrpcService<PaymentSubmissionGrpcService>();
app.MapGrpcService<TaxFeePolicyGrpcService>();
app.MapGrpcService<RestaurantReportGrpcService>();

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
