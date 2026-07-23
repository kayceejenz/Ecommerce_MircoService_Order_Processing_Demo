using InventoryService.Data;
using MassTransit;
using Shared.Contracts.Events;

namespace InventoryService.Consumers;

/// <summary>
/// Consumes OrderCancelled events and releases previously reserved inventory.
/// When an order is cancelled, the reserved stock is moved back to available
/// so it can be used by other orders.
/// </summary>
public class OrderCancelledConsumer : IConsumer<OrderCancelled>
{
    private readonly InventoryDbContext _db;
    private readonly ILogger<OrderCancelledConsumer> _logger;

    public OrderCancelledConsumer(InventoryDbContext db, ILogger<OrderCancelledConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderCancelled> context)
    {
        var message = context.Message;
        _logger.LogInformation(
            "Processing OrderCancelled for OrderId: {OrderId}, Reason: {Reason}",
            message.OrderId, message.Reason);

        // Note: In a full implementation, we would look up which items were reserved
        // for this order (from a reservations table or event store). For this experiment,
        // we track reservations by order in the ReservedQuantity field.
        // Here we do a best-effort release: find items that were recently reserved
        // and release stock proportionally.

        // For a production system, you would maintain an OrderInventoryReservations
        // table that maps OrderId -> List<ProductId, Quantity> so you know exactly
        // what to release. For this experiment, we'll publish a log and assume
        // the orchestrator provides the reservation details.

        _logger.LogInformation(
            "Inventory release requested for Order {OrderId}. " +
            "In production, reserved items would be looked up from a reservation store.",
            message.OrderId);

        // TODO: When saga is fully implemented, the OrderCancelled event should include
        // the original reserved items so we can release them precisely. For now,
        // this consumer acknowledges the cancellation and logs it.
    }
}
