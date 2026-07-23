using InventoryService.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace InventoryService.Data;

/// <summary>
/// EF Core DbContext for the Inventory database.
/// Provides access to the InventoryItems table and handles change tracking.
/// </summary>
public class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<InventoryItem>(entity =>
        {
            entity.ToTable("inventory_items");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProductName)
                .IsRequired()
                .HasMaxLength(200);
            entity.Property(e => e.AvailableQuantity)
                .HasDefaultValue(0);
            entity.Property(e => e.ReservedQuantity)
                .HasDefaultValue(0);
            entity.Property(e => e.ReorderThreshold)
                .HasDefaultValue(10);
            entity.HasIndex(e => e.ProductName)
                .HasDatabaseName("idx_inventory_product_name");
        });
    }
}
