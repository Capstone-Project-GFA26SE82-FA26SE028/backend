using Microsoft.EntityFrameworkCore;

namespace Ordering.DAL.Models;

public partial class OrderingDbContext
{
    public DbSet<TableTransfer> TableTransfers => Set<TableTransfer>();

    private static void ConfigureTableTransfers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TableTransfer>(entity =>
        {
            entity.ToTable("TableTransfer");
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
