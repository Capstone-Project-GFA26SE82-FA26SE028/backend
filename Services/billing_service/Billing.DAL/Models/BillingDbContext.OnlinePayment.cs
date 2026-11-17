using Microsoft.EntityFrameworkCore;

namespace Billing.DAL.Models;

public partial class BillingDbContext
{
    protected override bool IsRestaurantEntity(Type type)
    {
        // Method codes (Cash/BankTransfer) are a shared read-only dictionary, not balances.
        return type != typeof(PaymentMethod);
    }

    public DbSet<OnlinePayment> OnlinePayments { get; set; }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OnlinePayment>(entity =>
        {
            entity.HasKey(value => value.OrderId);
            entity.HasIndex(value => value.ProviderOrderCode).IsUnique();
            entity.HasIndex(value => value.TransactionReference).IsUnique().HasFilter("[TransactionReference] IS NOT NULL");
            entity.HasIndex(value => value.RefundReference).IsUnique().HasFilter("[RefundReference] IS NOT NULL");
            entity.Property(value => value.Amount).HasPrecision(18, 2);
            entity.Property(value => value.Status).HasMaxLength(30);
            entity.Property(value => value.LinkId).HasMaxLength(100);
            entity.Property(value => value.CheckoutUrl).HasMaxLength(2048);
            entity.Property(value => value.TransactionReference).HasMaxLength(255);
            entity.Property(value => value.RefundReference).HasMaxLength(255);
            entity.Property(value => value.RefundEvidence).HasMaxLength(2048);
            entity.Property(value => value.RefundReason).HasMaxLength(1000);
        });
    }
}
