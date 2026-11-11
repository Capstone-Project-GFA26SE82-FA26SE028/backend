using ApiGateway.Settings;
using Backend.Contracts.Billing;

namespace ApiGateway.Extensions;

public static class BillingServiceExtensions
{
    public static IServiceCollection AddBillingClients(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BillingClientSettings>()
            .Bind(configuration.GetSection("BillingClient"))
            .Validate(value => value.TimeoutSeconds is > 0 and <= 120, "Thời gian chờ dịch vụ thanh toán phải từ 1 đến 120 giây.")
            .ValidateOnStart();
        services.AddOptions<PayOsGatewaySettings>()
            .Bind(configuration.GetSection("PayOsGateway"))
            .Validate(value => value.TimeoutSeconds is > 0 and <= 120, "Thời gian chờ cổng PayOS phải từ 1 đến 120 giây.")
            .ValidateOnStart();
        var address = configuration["GrpcServices:Billing"];
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Vui lòng cấu hình GrpcServices:Billing.");
        }

        services.AddGrpcClient<CashRegisterGrpc.CashRegisterGrpcClient>(options =>
        {
            options.Address = uri;
        });
        services.AddGrpcClient<CashShiftGrpc.CashShiftGrpcClient>(options =>
        {
            options.Address = uri;
        });
        services.AddGrpcClient<BillGrpc.BillGrpcClient>(options =>
        {
            options.Address = uri;
        });
        services.AddGrpcClient<TaxFeePolicyGrpc.TaxFeePolicyGrpcClient>(options =>
        {
            options.Address = uri;
        });
        services.AddGrpcClient<BillCheckoutGrpc.BillCheckoutGrpcClient>(options =>
        {
            options.Address = uri;
        });
        services.AddGrpcClient<CashHandoverGrpc.CashHandoverGrpcClient>(options =>
        {
            options.Address = uri;
        });
        services.AddGrpcClient<PaymentGrpc.PaymentGrpcClient>(options =>
        {
            options.Address = uri;
        });
        services.AddGrpcClient<CashMovementGrpc.CashMovementGrpcClient>(options =>
        {
            options.Address = uri;
        });
        services.AddGrpcClient<ShiftReportGrpc.ShiftReportGrpcClient>(options =>
        {
            options.Address = uri;
        });
        services.AddGrpcClient<PayOsPaymentGrpc.PayOsPaymentGrpcClient>(options =>
        {
            options.Address = uri;
        });
        services.AddGrpcClient<PaymentWebhookGrpc.PaymentWebhookGrpcClient>(options =>
        {
            options.Address = uri;
        });
        return services;
    }
}
