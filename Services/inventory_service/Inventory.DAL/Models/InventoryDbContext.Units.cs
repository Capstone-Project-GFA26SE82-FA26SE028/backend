using Microsoft.EntityFrameworkCore;

namespace Inventory.DAL.Models;

public partial class InventoryDbContext
{
    public DbSet<Unit> Units { get; set; }
    public DbSet<IngredientUnit> IngredientUnits { get; set; }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        ConfigureIssues(modelBuilder);
        modelBuilder.Entity<Unit>(entity =>
        {
            entity.ToTable("Unit");
            entity.HasKey(x => x.UnitId);
            entity.Property(x => x.UnitId).ValueGeneratedNever();
            entity.Property(x => x.Code).HasMaxLength(50).IsUnicode(false).IsRequired();
            entity.HasIndex(x => x.Code).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
        });
        modelBuilder.Entity<IngredientUnit>(entity =>
        {
            entity.ToTable("IngredientUnit");
            entity.HasKey(x => new { x.IngredientId, x.UnitId });
            entity.Property(x => x.ConversionFactor).HasPrecision(18, 6);
            entity.HasOne<Ingredient>().WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Unit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<GoodsReceiptItem>(entity =>
        {
            entity.Property(x => x.InputQuantity).HasPrecision(18, 6);
            entity.Property(x => x.ConversionFactorSnapshot).HasPrecision(18, 6);
            entity.HasOne<Unit>().WithMany().HasForeignKey(x => x.InputUnitId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
