using Billing.DAL.Models;
using Billing.DAL.Interfaces;
using Billing.DAL.Implements;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Persistence.Interfaces;
using Shared.Persistence.Repositories;
using Shared.Persistence.UnitOfWorks;

namespace Billing.DAL.Extensions;

public static class PersistenceExtensions
{
    public static IServiceCollection AddBillingPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<BillingDbContext>(options =>
        {
            options.UseSqlServer(connectionString);
        });
        services.AddScoped<IUnitOfWork, UnitOfWork<BillingDbContext>>();
        services.AddScoped<ICashRegisterRepository, CashRegisterRepository>();
        services.AddScoped<IBillRepository, BillRepository>();
        services.AddScoped<ITaxFeePolicyRepository, TaxFeePolicyRepository>();
        services.AddScoped<IGenericRepository<global::Billing.DAL.Models.Bill>, GenericRepository<global::Billing.DAL.Models.Bill, BillingDbContext>>();
        services.AddScoped<IGenericRepository<global::Billing.DAL.Models.BillAdjustment>, GenericRepository<global::Billing.DAL.Models.BillAdjustment, BillingDbContext>>();
        services.AddScoped<IGenericRepository<global::Billing.DAL.Models.BillLine>, GenericRepository<global::Billing.DAL.Models.BillLine, BillingDbContext>>();
        services.AddScoped<IGenericRepository<global::Billing.DAL.Models.CashHandover>, GenericRepository<global::Billing.DAL.Models.CashHandover, BillingDbContext>>();
        services.AddScoped<IGenericRepository<global::Billing.DAL.Models.CashMovement>, GenericRepository<global::Billing.DAL.Models.CashMovement, BillingDbContext>>();
        services.AddScoped<IGenericRepository<global::Billing.DAL.Models.CashRegister>, GenericRepository<global::Billing.DAL.Models.CashRegister, BillingDbContext>>();
        services.AddScoped<IGenericRepository<global::Billing.DAL.Models.CashShift>, GenericRepository<global::Billing.DAL.Models.CashShift, BillingDbContext>>();
        services.AddScoped<ICashShiftRepository, CashShiftRepository>();
        services.AddScoped<IGenericRepository<global::Billing.DAL.Models.DiscountLimit>, GenericRepository<global::Billing.DAL.Models.DiscountLimit, BillingDbContext>>();
        services.AddScoped<IGenericRepository<global::Billing.DAL.Models.ElectronicInvoice>, GenericRepository<global::Billing.DAL.Models.ElectronicInvoice, BillingDbContext>>();
        services.AddScoped<IGenericRepository<global::Billing.DAL.Models.Payment>, GenericRepository<global::Billing.DAL.Models.Payment, BillingDbContext>>();
        services.AddScoped<IGenericRepository<global::Billing.DAL.Models.PaymentMethod>, GenericRepository<global::Billing.DAL.Models.PaymentMethod, BillingDbContext>>();
        services.AddScoped<IGenericRepository<global::Billing.DAL.Models.PaymentProviderConfig>, GenericRepository<global::Billing.DAL.Models.PaymentProviderConfig, BillingDbContext>>();
        services.AddScoped<IGenericRepository<global::Billing.DAL.Models.PaymentWebhook>, GenericRepository<global::Billing.DAL.Models.PaymentWebhook, BillingDbContext>>();
        services.AddScoped<IGenericRepository<global::Billing.DAL.Models.Promotion>, GenericRepository<global::Billing.DAL.Models.Promotion, BillingDbContext>>();
        services.AddScoped<IGenericRepository<global::Billing.DAL.Models.PromotionTarget>, GenericRepository<global::Billing.DAL.Models.PromotionTarget, BillingDbContext>>();
        services.AddScoped<IGenericRepository<global::Billing.DAL.Models.ReceiptPrint>, GenericRepository<global::Billing.DAL.Models.ReceiptPrint, BillingDbContext>>();
        services.AddScoped<IGenericRepository<global::Billing.DAL.Models.Refund>, GenericRepository<global::Billing.DAL.Models.Refund, BillingDbContext>>();
        services.AddScoped<IGenericRepository<global::Billing.DAL.Models.RefundLine>, GenericRepository<global::Billing.DAL.Models.RefundLine, BillingDbContext>>();
        services.AddScoped<IGenericRepository<global::Billing.DAL.Models.ShiftReport>, GenericRepository<global::Billing.DAL.Models.ShiftReport, BillingDbContext>>();
        services.AddScoped<IGenericRepository<global::Billing.DAL.Models.TaxFeePolicy>, GenericRepository<global::Billing.DAL.Models.TaxFeePolicy, BillingDbContext>>();
        services.AddScoped<IGenericRepository<global::Billing.DAL.Models.Voucher>, GenericRepository<global::Billing.DAL.Models.Voucher, BillingDbContext>>();
        services.AddScoped<IGenericRepository<global::Billing.DAL.Models.VoucherRedemption>, GenericRepository<global::Billing.DAL.Models.VoucherRedemption, BillingDbContext>>();

        return services;
    }
}
