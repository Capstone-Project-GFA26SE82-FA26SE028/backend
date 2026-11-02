using Billing.API.Workers;
using Backend.Contracts.Restaurant;
using Billing.API.Clients;
using Billing.API.GrpcServices;
using Billing.API.Settings;
using Billing.BLL.Interfaces;
using Billing.BLL.Implements;
using Billing.Common.Settings;

namespace Billing.API.Extensions;

public static class BillingServiceExtensions
{
    public static IServiceCollection AddBillingServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddGrpc(options =>
        {
            options.Interceptors.Add<BillingInterceptor>();
        });
        services.AddHttpContextAccessor();
        services.Configure<PayOsSettings>(configuration.GetSection("PayOs"));
        services.AddHttpClient<IPayOsClient, PayOsClient>(http =>
        {
            http.MaxResponseContentBufferSize = 262144;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        }).RedactLoggedHeaders(_ => true);
        services.AddScoped<IPayOsPaymentService, PayOsPaymentService>();
        services.AddScoped<IPaymentWebhookService, PaymentWebhookService>();
        services.AddHostedService<PayOsReconciliationWorker>();
        services.AddOptions<CashRegisterAccessSettings>()
            .Bind(configuration.GetSection("CashRegisterAccess"))
            .Validate(value => value.ManagerRoleIds.Length > 0 && value.CashierRoleIds.Length > 0
                && value.ManagerRoleIds.All(role => !string.IsNullOrWhiteSpace(role))
                && value.CashierRoleIds.All(role => !string.IsNullOrWhiteSpace(role))
                && !value.ManagerRoleIds.Intersect(value.CashierRoleIds, StringComparer.Ordinal).Any(),
                "Vui lòng cấu hình ID vai trò quản lý và thu ngân riêng biệt để truy cập quầy thu ngân.")
            .ValidateOnStart();
        services.AddOptions<RestaurantAccessSettings>()
            .Bind(configuration.GetSection("RestaurantAccess"))
            .Validate(value => value.TimeoutSeconds is > 0 and <= 30, "Thời gian chờ truy cập dịch vụ nhà hàng phải từ 1 đến 30 giây.")
            .ValidateOnStart();
        var address = configuration["GrpcServices:Restaurant"];
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Vui lòng cấu hình GrpcServices:Restaurant.");
        }

        services.AddGrpcClient<RestaurantAccessGrpc.RestaurantAccessGrpcClient>(options =>
        {
            options.Address = uri;
        });
        services.AddGrpcClient<Backend.Contracts.Ordering.BillingOrderGrpc.BillingOrderGrpcClient>(options =>
        {
            var orderingAddress = configuration["GrpcServices:Ordering"];
            if (!Uri.TryCreate(orderingAddress, UriKind.Absolute, out var orderingUri)
                || (orderingUri.Scheme != Uri.UriSchemeHttp && orderingUri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("Vui lòng cấu hình GrpcServices:Ordering.");
            }

            options.Address = orderingUri;
        });
        services.AddGrpcClient<Backend.Contracts.Ordering.CheckoutLockGrpc.CheckoutLockGrpcClient>(options =>
        {
            options.Address = new Uri(configuration["GrpcServices:Ordering"]!);
        });
        services.AddScoped<ICheckoutSourceClient, CheckoutSourceClient>();
        services.AddScoped<IBillCheckoutService, BillCheckoutService>();
        services.AddScoped<IBillSourceClient, BillSourceClient>();
        services.Configure<BillSettings>(configuration.GetSection("Billing"));
        services.AddScoped<IBillingAccess, RestaurantBillingAccess>();
        services.AddScoped<ICashRegisterService, CashRegisterService>();
        services.AddScoped<ICashShiftService, CashShiftService>();
        services.AddOptions<CashPaymentSettings>().Bind(configuration.GetSection("CashPayment"))
            .Validate(value => !string.IsNullOrWhiteSpace(value.MethodCode) && value.MethodCode.Length <= 50
                && value.MethodCode == value.MethodCode.Trim() && value.MethodCode.All(character => character is >= '!' and <= '~'),
                "Vui lòng cấu hình CashPayment:MethodCode.")
            .ValidateOnStart();
        services.AddScoped<IShiftReportService, ShiftReportService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<ICashMovementService, CashMovementService>();
        services.AddScoped<IBillService, BillService>();
        services.AddScoped<ITaxFeePolicyService, TaxFeePolicyService>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }
}
