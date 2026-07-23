using Microsoft.EntityFrameworkCore;
using OrderService.Sagas;

namespace OrderService.ReadModel;

/// <summary>
/// DbContext for saga state persistence.
/// </summary>
public class SagaStateDbContext(DbContextOptions<SagaStateDbContext> options) : DbContext(options)
{
    public DbSet<OrderSagaState> OrderSagaState => Set<OrderSagaState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // MassTransit saga configuration
        // Maps OrderSagaState to the saga state table
        modelBuilder.Entity<OrderSagaState>(entity =>
        {
            entity.HasKey(e => e.CorrelationId);
            entity.Property(e => e.CurrentState).HasMaxLength(50);
            entity.Property(e => e.FailureReason).HasMaxLength(500);
        });
    }
}
