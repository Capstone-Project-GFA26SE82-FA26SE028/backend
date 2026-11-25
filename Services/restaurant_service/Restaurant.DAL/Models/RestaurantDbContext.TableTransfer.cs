using Microsoft.EntityFrameworkCore;

namespace Restaurant.DAL.Models;

public partial class RestaurantDbContext
{
    public DbSet<TableTransferSession> TableTransfers => Set<TableTransferSession>();

    private static void ConfigureTableTransfers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TableTransferSession>(entity =>
        {
            entity.ToTable("TableTransferSession");
            entity.HasKey(value => value.OperationId);
            entity.Property(value => value.OperationId).ValueGeneratedNever();
            entity.Property(value => value.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(value => value.Mode).HasMaxLength(10).IsRequired();
            entity.Property(value => value.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(value => value.Status).HasMaxLength(20).IsRequired();
            entity.HasIndex(value => new { value.SourceSessionId, value.Status });
            entity.HasIndex(value => new { value.TargetSessionId, value.Status });
        });
    }
}
