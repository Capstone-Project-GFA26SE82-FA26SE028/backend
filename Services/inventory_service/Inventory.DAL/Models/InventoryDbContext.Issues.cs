using Microsoft.EntityFrameworkCore;

namespace Inventory.DAL.Models;

public partial class InventoryDbContext
{
    public DbSet<StockIssue> StockIssues { get; set; }

    private static void ConfigureIssues(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StockIssue>(entity =>
        {
            entity.ToTable("StockIssue");
            entity.HasKey(x => x.StockIssueId);
            entity.Property(x => x.StockIssueId).ValueGeneratedNever();
            entity.Property(x => x.CreatedAt).HasColumnType("datetime2");
            entity.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false).IsRequired();
            entity.HasMany(x => x.Movements).WithOne().HasForeignKey(x => x.StockIssueId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<StockMovement>().HasIndex(x => x.StockIssueId);
        modelBuilder.Entity<StockMovement>().HasIndex(x => x.GoodsReceiptItemId);
    }
}
