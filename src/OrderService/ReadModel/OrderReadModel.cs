using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OrderService.ReadModel;

/// <summary>
/// Denormalized order view for fast queries.
/// Updated by event projections from EventStoreDB.
/// </summary>
public class OrderReadModel
{
    [Key]
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string ItemsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? CancellationReason { get; set; }
    public long LastEventSequence { get; set; }
}

/// <summary>
/// EF Core DbContext for the Order read model database.
/// </summary>
public class OrderReadDbContext(DbContextOptions<OrderReadDbContext> options) : DbContext(options)
{
    public DbSet<OrderReadModel> Orders => Set<OrderReadModel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<OrderReadModel>(entity =>
        {
            entity.ToTable("orders");
            entity.HasKey(e => e.OrderId);

            entity.Property(e => e.Status)
                .HasMaxLength(50);

            entity.Property(e => e.TotalAmount)
                .HasColumnType("decimal(18,2)");

            entity.HasIndex(e => e.CustomerId)
                .HasDatabaseName("idx_orders_customer_id");

            entity.HasIndex(e => e.Status)
                .HasDatabaseName("idx_orders_status");

            entity.HasIndex(e => e.CreatedAt)
                .HasDatabaseName("idx_orders_created_at");
        });
    }
}
