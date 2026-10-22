using Inventory.DAL.Extensions;
using Inventory.DAL.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<Inventory.API.GrpcServices.InventoryInterceptor>();
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddGrpcClient<Backend.Contracts.Identity.UserGrpc.UserGrpcClient>(options =>
{
    options.Address = new Uri(builder.Configuration["GrpcServices:Identity"]
        ?? throw new InvalidOperationException("Configure GrpcServices:Identity."));
});
builder.Services.AddScoped<Inventory.BLL.Interfaces.IInventoryAccess, Inventory.API.Clients.IdentityInventoryAccess>();
builder.Services.AddScoped<Inventory.BLL.Services.IngredientService>();
builder.Services.AddScoped<Inventory.BLL.Services.StockLotService>();
builder.Services.AddScoped<Inventory.BLL.Services.StockIssueService>();
builder.Services.AddScoped<Inventory.BLL.Services.UnitService>();
builder.Services.AddScoped<Inventory.BLL.Services.IngredientUnitService>();
builder.Services.AddScoped<Inventory.BLL.Services.GoodsReceiptService>();
builder.Services.AddScoped<Inventory.BLL.Services.StockMovementService>();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection in appsettings.json or environment variables.");
}

builder.Services.AddInventoryPersistence(connectionString);

var app = builder.Build();

app.MapGrpcService<Inventory.API.GrpcServices.IngredientGrpcService>();
app.MapGrpcService<Inventory.API.GrpcServices.StockLotGrpcService>();
app.MapGrpcService<Inventory.API.GrpcServices.StockIssueGrpcService>();
app.MapGrpcService<Inventory.API.GrpcServices.UnitGrpcService>();
app.MapGrpcService<Inventory.API.GrpcServices.IngredientUnitGrpcService>();
app.MapGrpcService<Inventory.API.GrpcServices.GoodsReceiptGrpcService>();
app.MapGrpcService<Inventory.API.GrpcServices.StockMovementGrpcService>();

app.MapGet("/health/live", () =>
{
    return Results.Ok();
});

app.MapGet("/health/ready", async (InventoryDbContext db, CancellationToken cancellationToken) =>
{
    var canConnect = await db.Database.CanConnectAsync(cancellationToken);
    return canConnect ? Results.Ok() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
});

app.Run();
