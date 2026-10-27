using ApiGateway.Settings;
using Backend.Contracts.Billing;

namespace ApiGateway.Extensions;

public static class BillingServiceExtensions
{
    public static IServiceCollection AddBillingClients(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BillingClientSettings>()
            .Bind(configuration.GetSection("BillingClient"))
            .Validate(value => value.TimeoutSeconds is > 0 and <= 120, "Billing client timeout must be 1-120 seconds.")
            .ValidateOnStart();
        var address = configuration["GrpcServices:Billing"];
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Configure GrpcServices:Billing.");
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
        return services;
    }
}
