using Microsoft.EntityFrameworkCore;

namespace Identity.Domain.Models;

public partial class IdentityDbContext
{
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationRecipient> NotificationRecipients => Set<NotificationRecipient>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<NotificationSetting> NotificationSettings => Set<NotificationSetting>();

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("Notification");
            entity.HasKey(value => value.NotificationId);
            entity.HasIndex(value => value.EventKey).IsUnique();
            entity.HasIndex(value => new { value.RestaurantId, value.CreatedAt });
            entity.Property(value => value.EventKey).HasMaxLength(200);
            entity.Property(value => value.Type).HasMaxLength(80);
            entity.Property(value => value.Title).HasMaxLength(200);
            entity.Property(value => value.Message).HasMaxLength(2000);
            entity.Property(value => value.EntityType).HasMaxLength(40);
        });
        modelBuilder.Entity<NotificationRecipient>(entity =>
        {
            entity.ToTable("NotificationRecipient");
            entity.HasKey(value => new { value.NotificationId, value.UserId });
            entity.HasIndex(value => new { value.UserId, value.ReadAt });
            entity.HasOne(value => value.Notification).WithMany(value => value.Recipients)
                .HasForeignKey(value => value.NotificationId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Announcement>(entity =>
        {
            entity.ToTable("Announcement");
            entity.HasKey(value => value.AnnouncementId);
            entity.HasIndex(value => new { value.RestaurantId, value.CreatedAt });
            entity.Property(value => value.Title).HasMaxLength(200);
            entity.Property(value => value.Message).HasMaxLength(2000);
            entity.Property(value => value.RecipientRoles).HasMaxLength(200);
            entity.Property(value => value.Version).IsConcurrencyToken();
        });
        modelBuilder.Entity<NotificationSetting>(entity =>
        {
            entity.ToTable("NotificationSetting");
            entity.HasKey(value => new { value.RestaurantId, value.Type });
            entity.Property(value => value.Type).HasMaxLength(80);
        });
    }
}
