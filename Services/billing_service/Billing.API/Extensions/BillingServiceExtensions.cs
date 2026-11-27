using Billing.API.Workers;
using Backend.Contracts.Restaurant;
using Billing.API.Clients;
using Billing.API.GrpcServices;
using Billing.API.Settings;
using Billing.BLL.Interfaces;
using Billing.BLL.Implements;
using Billing.Common.Settings;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Billing.API.Extensions;

public static class BillingServiceExtensions
{
    public static IServiceCollection AddBillingServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddGrpc(options =>
        {
            options.Interceptors.Add<RestaurantIsolation.RestaurantScopeInterceptor>();
            options.Interceptors.Add<BillingInterceptor>();
        });
        services.AddHttpContextAccessor();
        services.Configure<PayOsSettings>(configuration.GetSection("PayOs"));
        services.Configure<PaymentProviderConfigurationSettings>(configuration.GetSection("PaymentProviderConfiguration"));
        services.AddScoped<IOptions<PayOsSettings>, RestaurantPayOsOptions>();
        services.AddScoped<IPaymentProviderSecrets, PaymentProviderSecrets>();
        services.AddScoped<IPaymentProviderConfigService, PaymentProviderConfigService>();
        var protection = services.AddDataProtection().SetApplicationName("RestaurantBilling");
        var keyDirectory = configuration["PaymentProviderConfiguration:KeyDirectory"];
        if (!string.IsNullOrWhiteSpace(keyDirectory))
        {
            protection.PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
        }

        services.AddHttpClient<PayOsClient>(http =>
        {
            http.MaxResponseContentBufferSize = 262144;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        }).RedactLoggedHeaders(_ => true);
        services.AddScoped<IPayOsClient>(sp => sp.GetRequiredService<PayOsClient>());
        services.AddScoped<IPayOsTestClient>(sp => sp.GetRequiredService<PayOsClient>());
        services.AddScoped<IPayOsPaymentService, PayOsPaymentService>();
        services.AddScoped<IOnlinePaymentService, OnlinePaymentService>();
        services.AddScoped<IPaymentWebhookService, PaymentWebhookService>();
        services.AddHostedService<PayOsReconciliationWorker>();
        services.AddOptions<CashRegisterAccessSettings>()
            .Bind(configuration.GetSection("CashRegisterAccess"))
            .Validate(value => value.ManagerRoleIds.Length > 0 && value.CashierRoleIds.Length > 0
                && value.ManagerRoleIds.All(role => !string.IsNullOrWhiteSpace(role))
                && value.CashierRoleIds.All(role => !string.IsNullOrWhiteSpace(role))
                && !value.ManagerRoleIds.Intersect(value.CashierRoleIds, StringComparer.Ordinal).Any(),
                "Vui lòng cấu hình ID vai trò quản lý và thu ngân riêng biệt để truy cập quầy thu ngân.")
            .Validate(value => value.WaiterRoleIds.Length > 0
                && value.WaiterRoleIds.All(role => !string.IsNullOrWhiteSpace(role))
                && !value.WaiterRoleIds.Intersect(value.CashierRoleIds.Concat(value.ManagerRoleIds), StringComparer.Ordinal).Any(),
                "Vui lòng cấu hình ID vai trò phục vụ riêng biệt cho nghiệp vụ thu hộ.")
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
        services.AddGrpcClient<ServiceRequestGrpc.ServiceRequestGrpcClient>(options =>
        {
            options.Address = uri;
        });
        services.AddScoped<IPaymentReviewClient, PaymentReviewClient>();
        services.AddGrpcClient<BillingSessionGrpc.BillingSessionGrpcClient>(options =>
        {
            options.Address = uri;
        });
        services.AddScoped<IBillingSessionDisplayClient, BillingSessionDisplayClient>();
        services.AddGrpcClient<PaidSessionClosureGrpc.PaidSessionClosureGrpcClient>(options =>
        {
            options.Address = uri;
        });
        services.AddScoped<IPaidSessionClosureClient, PaidSessionClosureClient>();
        services.AddScoped<IPaymentSettlementService, PaymentSettlementService>();
        services.AddScoped<IPaymentSubmissionService, PaymentSubmissionService>();
        services.AddGrpcClient<Backend.Contracts.Customer.CustomerGrpc.CustomerGrpcClient>(options =>
        {
            var customerAddress = configuration["GrpcServices:Customer"] ?? "https://localhost:5005";
            if (Uri.TryCreate(customerAddress, UriKind.Absolute, out var customerUri))
            {
                options.Address = customerUri;
            }
        });
        services.AddScoped<ICustomerLoyaltyClient, CustomerLoyaltyClient>();
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
        services.AddScoped<IRestaurantReportContextClient, RestaurantReportContextClient>();
        services.AddScoped<IRestaurantReportService, RestaurantReportService>();
        services.AddScoped<IRestaurantSalesReportService, RestaurantSalesReportService>();
        services.AddScoped<IRestaurantDishReportService, RestaurantDishReportService>();
        services.AddScoped<IRestaurantReportItemsClient, RestaurantReportItemsClient>();
        services.AddScoped<IRestaurantReportOrderClient, RestaurantReportOrderClient>();
        services.AddScoped<ICashRegisterService, CashRegisterService>();
        services.AddScoped<ICashShiftService, CashShiftService>();
        services.AddOptions<CashPaymentSettings>().Bind(configuration.GetSection("CashPayment"))
            .Validate(value => !string.IsNullOrWhiteSpace(value.MethodCode) && value.MethodCode.Length <= 50
                && value.MethodCode == value.MethodCode.Trim() && value.MethodCode.All(character => character is >= '!' and <= '~'),
                "Vui lòng cấu hình CashPayment:MethodCode.")
            .ValidateOnStart();
        services.AddScoped<IShiftReportService, ShiftReportService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<ICashHandoverService, CashHandoverService>();
        services.AddScoped<ICashMovementService, CashMovementService>();
        services.AddScoped<IBillService, BillService>();
        services.AddScoped<IPromotionService, PromotionService>();
        services.AddScoped<IVoucherService, VoucherService>();
        services.AddScoped<ITaxFeePolicyService, TaxFeePolicyService>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }
}
