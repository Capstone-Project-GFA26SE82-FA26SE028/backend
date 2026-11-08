using Backend.Contracts.Identity;
using Grpc.Core;
using ApiGateway.Settings;
using ApiGateway.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables().AddCommandLine(args);
ApiGateway.Extensions.AIServiceExtensions.AddAssistantClients(builder.Services, builder.Configuration);
ApiGateway.Extensions.BillingServiceExtensions.AddBillingClients(builder.Services, builder.Configuration);
builder.Services.AddGrpcClient<Backend.Contracts.Inventory.StockLotGrpc.StockLotGrpcClient>(options =>
{
    options.Address = new Uri(builder.Configuration["GrpcServices:Inventory"]!);
});
builder.Services.AddGrpcClient<Backend.Contracts.Inventory.StockIssueGrpc.StockIssueGrpcClient>(options =>
{
    options.Address = new Uri(builder.Configuration["GrpcServices:Inventory"]!);
});
builder.Services.AddGrpcClient<Backend.Contracts.Inventory.UnitGrpc.UnitGrpcClient>(options =>
{
    options.Address = new Uri(builder.Configuration["GrpcServices:Inventory"]!);
});
builder.Services.AddGrpcClient<Backend.Contracts.Inventory.IngredientUnitGrpc.IngredientUnitGrpcClient>(options =>
{
    options.Address = new Uri(builder.Configuration["GrpcServices:Inventory"]!);
});
builder.Services.AddGrpcClient<Backend.Contracts.Inventory.IngredientGrpc.IngredientGrpcClient>(options =>
{
    options.Address = new Uri(builder.Configuration["GrpcServices:Inventory"]
        ?? throw new InvalidOperationException("Vui lòng cấu hình GrpcServices:Inventory."));
});
builder.Services.AddGrpcClient<Backend.Contracts.Inventory.GoodsReceiptGrpc.GoodsReceiptGrpcClient>(options =>
{
    options.Address = new Uri(builder.Configuration["GrpcServices:Inventory"]
        ?? throw new InvalidOperationException("Vui lòng cấu hình GrpcServices:Inventory."));
});
builder.Services.AddGrpcClient<Backend.Contracts.Inventory.StockMovementGrpc.StockMovementGrpcClient>(options =>
{
    options.Address = new Uri(builder.Configuration["GrpcServices:Inventory"]
        ?? throw new InvalidOperationException("Vui lòng cấu hình GrpcServices:Inventory."));
});
builder.Services.AddOptions<OrderingClientSettings>().Bind(builder.Configuration.GetSection("OrderingClient"))
    .Validate(settings => settings.TimeoutSeconds is > 0 and <= 120, "Thời gian chờ OrderingClient phải từ 1 đến 120 giây.")
    .ValidateOnStart();
builder.Services.AddGrpcClient<Backend.Contracts.Ordering.CartGrpc.CartGrpcClient>(options =>
{
    options.Address = new Uri(builder.Configuration["GrpcServices:Ordering"]
        ?? throw new InvalidOperationException("Vui lòng cấu hình GrpcServices:Ordering."));
});
builder.Services.AddGrpcClient<Backend.Contracts.Ordering.OrderGrpc.OrderGrpcClient>(options =>
{
    options.Address = new Uri(builder.Configuration["GrpcServices:Ordering"]
        ?? throw new InvalidOperationException("Vui lòng cấu hình GrpcServices:Ordering."));
});
builder.Services.AddGrpcClient<Backend.Contracts.Ordering.KitchenStationGrpc.KitchenStationGrpcClient>(options =>
{
    options.Address = new Uri(builder.Configuration["GrpcServices:Ordering"]
        ?? throw new InvalidOperationException("Vui lòng cấu hình GrpcServices:Ordering."));
});
builder.Services.AddGrpcClient<Backend.Contracts.Ordering.KitchenTaskGrpc.KitchenTaskGrpcClient>(options =>
{
    options.Address = new Uri(builder.Configuration["GrpcServices:Ordering"]
        ?? throw new InvalidOperationException("Vui lòng cấu hình GrpcServices:Ordering."));
});
builder.Services.AddOptions<CatalogClientSettings>()
    .Bind(builder.Configuration.GetSection("CatalogClient"))
    .Validate(settings => settings.TimeoutSeconds > 0,
        "CatalogClient:TimeoutSeconds phải lớn hơn 0.")
    .ValidateOnStart();

var catalogAddress = builder.Configuration["GrpcServices:Catalog"];
if (!Uri.TryCreate(catalogAddress, UriKind.Absolute, out var catalogUri)
    || (catalogUri.Scheme != Uri.UriSchemeHttp
        && catalogUri.Scheme != Uri.UriSchemeHttps))
{
    throw new InvalidOperationException("Vui lòng cấu hình GrpcServices:Catalog.");
}

builder.Services.AddGrpcClient<Backend.Contracts.Catalog.CategoryGrpc.CategoryGrpcClient>(options =>
{
    options.Address = catalogUri;
});
builder.Services.AddGrpcClient<Backend.Contracts.Catalog.OrderValidationGrpc.OrderValidationGrpcClient>(options =>
{
    options.Address = catalogUri;
});
builder.Services.AddGrpcClient<Backend.Contracts.Catalog.MenuGrpc.MenuGrpcClient>(options =>
{
    options.Address = catalogUri;
});
builder.Services.AddGrpcClient<Backend.Contracts.Catalog.ItemGrpc.ItemGrpcClient>(options =>
{
    options.Address = catalogUri;
});
builder.Services.AddGrpcClient<Backend.Contracts.Catalog.PriceGrpc.PriceGrpcClient>(options =>
{
    options.Address = catalogUri;
});

builder.Services.AddControllers().AddVietnameseValidation();
builder.Services.AddGrpcClient<Backend.Contracts.Catalog.ItemImageGrpc.ItemImageGrpcClient>(options =>
{
    options.Address = catalogUri;
});
builder.Services.AddGrpcClient<Backend.Contracts.Catalog.OptionGroupGrpc.OptionGroupGrpcClient>(options =>
{
    options.Address = catalogUri;
});
builder.Services.AddGrpcClient<Backend.Contracts.Catalog.OptionGrpc.OptionGrpcClient>(options =>
{
    options.Address = catalogUri;
});
builder.Services.AddGrpcClient<Backend.Contracts.Catalog.ItemOptionGroupGrpc.ItemOptionGroupGrpcClient>(options =>
{
    options.Address = catalogUri;
});
builder.Services.AddGrpcClient<Backend.Contracts.Catalog.RecipeGrpc.RecipeGrpcClient>(options =>
{
    options.Address = catalogUri;
});
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp",
        policy =>
        {
            policy.SetIsOriginAllowed(_ => true)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "API Gateway",
        Version = "v1"
    });
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Dán mã truy cập được trả về khi đăng nhập hoặc làm mới mã."
    });
    options.OperationFilter<ApiGateway.Swagger.BearerSecurityFilter>();
});

var identityAddress =
    builder.Configuration["GrpcServices:Identity"]
    ?? throw new InvalidOperationException(
        "Chưa cấu hình GrpcServices:Identity.");

builder.Services.AddGrpcClient<IdentityGrpc.IdentityGrpcClient>(
    options => options.Address = new Uri(identityAddress));

builder.Services.AddGrpcClient<UserGrpc.UserGrpcClient>(
    options => options.Address = new Uri(identityAddress));

builder.Services.AddGrpcClient<AuditLogGrpc.AuditLogGrpcClient>(
    options => options.Address = new Uri(identityAddress));
builder.Services.AddGrpcClient<SessionGrpc.SessionGrpcClient>(
    options => options.Address = new Uri(identityAddress));

builder.Services.AddGrpcClient<RoleGrpc.RoleGrpcClient>(
    options => options.Address = new Uri(identityAddress));

var restaurantAddress = builder.Configuration["GrpcServices:Restaurant"]
    ?? throw new InvalidOperationException("Vui lòng cấu hình GrpcServices:Restaurant.");
builder.Services.AddGrpcClient<Backend.Contracts.Restaurant.RestaurantGrpc.RestaurantGrpcClient>(options =>
{
    options.Address = new Uri(restaurantAddress);
});
builder.Services.AddGrpcClient<Backend.Contracts.Restaurant.AreaGrpc.AreaGrpcClient>(options =>
{
    options.Address = new Uri(restaurantAddress);
});
builder.Services.AddGrpcClient<Backend.Contracts.Restaurant.TableGrpc.TableGrpcClient>(options =>
{
    options.Address = new Uri(restaurantAddress);
});

builder.Services.AddGrpcClient<Backend.Contracts.Restaurant.QrGrpc.QrGrpcClient>(options =>
{
    options.Address = new Uri(restaurantAddress);
});

builder.Services.AddGrpcClient<Backend.Contracts.Restaurant.TableSessionGrpc.TableSessionGrpcClient>(options =>
{
    options.Address = new Uri(restaurantAddress);
});

builder.Services.AddGrpcClient<Backend.Contracts.Restaurant.GuestAccessGrpc.GuestAccessGrpcClient>(options =>
{
    options.Address = new Uri(restaurantAddress);
});

builder.Services.AddGrpcClient<Backend.Contracts.Restaurant.ReservationGrpc.ReservationGrpcClient>(options =>
{
    options.Address = new Uri(restaurantAddress);
});

builder.Services.AddGrpcClient<Backend.Contracts.Restaurant.RestaurantStaffGrpc.RestaurantStaffGrpcClient>(options =>
{
    options.Address = new Uri(restaurantAddress);
});

var customerAddress = builder.Configuration["GrpcServices:Customer"]
    ?? "https://localhost:5005";

builder.Services.AddGrpcClient<Backend.Contracts.Customer.CustomerGrpc.CustomerGrpcClient>(options =>
{
    options.Address = new Uri(customerAddress);
});

builder.Services.AddGrpcClient<Backend.Contracts.Customer.FeedbackGrpc.FeedbackGrpcClient>(options =>
{
    options.Address = new Uri(customerAddress);
});

var app = builder.Build();


if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
        options.SwaggerEndpoint("v1/swagger.json", "API Gateway v1"));
}
app.UseCors("AllowReactApp");
app.MapControllers();

app.MapGet("/api/identity/ping", async (
    IdentityGrpc.IdentityGrpcClient client,
    CancellationToken cancellationToken) =>
{
    try
    {
        var reply = await client.PingAsync(
            new PingRequest { Name = "ApiGateway" },
            deadline: DateTime.UtcNow.AddSeconds(5),
            cancellationToken: cancellationToken);

        return Results.Ok(new
        {
            message = reply.Message
        });
    }
    catch (RpcException ex)
        when (ex.StatusCode == StatusCode.Unavailable)
    {
        return Results.Problem(
            statusCode: 503,
            detail: "Dịch vụ định danh hiện không khả dụng.");
    }
    catch (RpcException ex)
        when (ex.StatusCode == StatusCode.DeadlineExceeded)
    {
        return Results.Problem(
            statusCode: 504,
            detail: "Dịch vụ định danh đã hết thời gian chờ.");
    }
});

app.Run();
