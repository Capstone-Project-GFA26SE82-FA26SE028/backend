using Microsoft.EntityFrameworkCore;

namespace Restaurant.DAL.Models;

public partial class RestaurantDbContext
{
    public DbSet<RestaurantRegistration> RestaurantRegistrations => Set<RestaurantRegistration>();

    private static void ConfigureRestaurantRegistrations(ModelBuilder builder)
    {
        builder.Entity<RestaurantRegistration>(entity =>
        {
            entity.ToTable("RestaurantRegistration");
            entity.HasKey(item => item.RegistrationId);
            entity.Property(item => item.RegistrationId).ValueGeneratedNever();
            entity.Property(item => item.Email).HasMaxLength(255);
            entity.Property(item => item.Status).HasMaxLength(20).IsUnicode(false);
            entity.Property(item => item.BusinessCertificateAssetId).HasMaxLength(100).IsUnicode(false);
            entity.Property(item => item.FoodSafetyCertificateAssetId).HasMaxLength(100).IsUnicode(false);
            entity.HasIndex(item => item.ApplicantUserRef).IsUnique();
            entity.HasIndex(item => item.RestaurantId).IsUnique().HasFilter("[RestaurantId] IS NOT NULL");
            entity.HasIndex(item => new { item.Status, item.SubmittedAt, item.RegistrationId });
            entity.HasOne<Restaurant>().WithMany().HasForeignKey(item => item.RestaurantId)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
