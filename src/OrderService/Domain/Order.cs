using Shared.Contracts.Events;

namespace OrderService.Domain;

/// <summary>
/// Represents an order in the system.
/// This is an EVENT-SOURCED aggregate - its state is derived from events.
/// </summary>
public class Order
{
    // These represent the CURRENT state of the order.
    // In event sourcing, these are computed by replaying events.

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public OrderStatus Status { get; private set; }
    public decimal TotalAmount { get; private set; }
    public List<OrderLineItem> Items { get; private set; } = new();
    public DateTime CreatedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public string? CancellationReason { get; private set; }

    // Track uncommitted events (events not yet saved to EventStoreDB)
    private readonly List<object> _uncommittedEvents = [];

    // Private constructor prevents direct instantiation.
    // Use Create() factory method to ensure invariants are enforced.
    // Internal constructor allows EventStoreRepository to rebuild via Replay
    internal Order() { }

    // This is the ONLY way to create a new order.
    // It validates inputs and raises the OrderCreated event.
    //
    // BUSINESS RULES ENFORCED:
    //   - Order must have at least one item
    //   - Quantities must be positive
    //   - Total is calculated from line items
    public static Order Create(Guid customerId, List<(Guid ProductId, int Quantity, decimal UnitPrice)> items)
    {
        // Business rule: must have items
        if (items.Count == 0)
            throw new InvalidOperationException("Order must contain at least one item");

        // Business rule: quantities must be positive
        if (items.Any(i => i.Quantity <= 0))
            throw new InvalidOperationException("All quantities must be positive");

        // Calculate total
        var totalAmount = items.Sum(i => i.Quantity * i.UnitPrice);

        // Create the order instance
        var order = new Order();
        order.Id = Guid.NewGuid();
        order.CustomerId = customerId;
        order.Status = OrderStatus.Created;
        order.TotalAmount = totalAmount;
        order.CreatedAt = DateTime.UtcNow;

        // Create line items
        order.Items = [.. items.Select(i => new OrderLineItem
        {
            ProductId = i.ProductId,
            Quantity = i.Quantity,
            UnitPrice = i.UnitPrice
        })];

        // Raise the event (this is what gets stored in EventStoreDB)
        order._uncommittedEvents.Add(new OrderPlaced
        {
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            Items = [.. order.Items.Select(i => new Shared.Contracts.Events.OrderItem
            {
                ProductId = i.ProductId,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            })],
            TotalAmount = order.TotalAmount,
            PlacedAt = order.CreatedAt
        });

        return order;
    }

    /// <summary>
    /// Mark inventory as reserved for this order.
    /// Called when InventoryService confirms stock is available.
    /// </summary>
    public void ConfirmInventoryReserved()
    {
        if (Status != OrderStatus.Created)
            throw new InvalidOperationException($"Cannot reserve inventory in status {Status}");

        Status = OrderStatus.InventoryReserved;
        // In a full implementation, this would raise an InventoryReservedEvent
    }

    /// <summary>
    /// Mark payment as successful.
    /// Called when PaymentService confirms payment.
    /// </summary>
    public void ConfirmPayment()
    {
        if (Status != OrderStatus.InventoryReserved)
            throw new InvalidOperationException($"Cannot confirm payment in status {Status}");

        Status = OrderStatus.PaymentProcessed;
        // In a full implementation, this would raise a PaymentProcessedEvent
    }

    /// <summary>
    /// Complete the order.
    /// Called after all steps succeed.
    /// </summary>
    public void Complete()
    {
        if (Status != OrderStatus.PaymentProcessed)
            throw new InvalidOperationException($"Cannot complete order in status {Status}");

        Status = OrderStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        // Would raise OrderConfirmed event
    }

    /// <summary>
    /// Cancel the order with a reason.
    /// Can be called at any step if something fails.
    /// </summary>
    public void Cancel(string reason)
    {
        if (Status == OrderStatus.Completed)
            throw new InvalidOperationException("Cannot cancel a completed order");

        Status = OrderStatus.Cancelled;
        CancellationReason = reason;

        _uncommittedEvents.Add(new OrderCancelled
        {
            OrderId = Id,
            CustomerId = CustomerId,
            Reason = reason,
            CancelledAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Get all uncommitted events that need to be saved to EventStoreDB.
    /// </summary>
    public IReadOnlyList<object> GetUncommittedEvents() => _uncommittedEvents.AsReadOnly();

    /// <summary>
    /// Mark all events as committed (after saving to EventStoreDB).
    /// </summary>
    public void ClearUncommittedEvents() => _uncommittedEvents.Clear();

    /// <summary>
    /// Rebuild order state from historical events.
    /// This is how event sourcing reconstructs current state.
    ///
    /// EXAMPLE:
    ///   Event 1: OrderCreated -> Status = Created, Total = $50
    ///   Event 2: InventoryReserved -> Status = InventoryReserved
    ///   Event 3: PaymentProcessed -> Status = PaymentProcessed
    ///   Event 4: OrderConfirmed -> Status = Completed
    ///
    /// After replaying all 4 events, the order has Status = Completed.
    /// </summary>
    public void Replay(object @event)
    {
        switch (@event)
        {
            case OrderPlaced e:
                Id = e.OrderId;
                CustomerId = e.CustomerId;
                Status = OrderStatus.Created;
                TotalAmount = e.TotalAmount;
                CreatedAt = e.PlacedAt;
                Items = [.. e.Items.Select(i => new OrderLineItem
                {
                    ProductId = i.ProductId,
                    Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice
                })];
                break;

            case OrderCancelled e:
                Status = OrderStatus.Cancelled;
                CancellationReason = e.Reason;
                break;
        }
    }
}

// Represents the lifecycle of an order through the saga.
//
// STATE MACHINE:
//   Created -> InventoryReserved -> PaymentProcessed -> Completed
//     |              |                    |
//     v              v                    v
//   Cancelled     Cancelled           Cancelled

public enum OrderStatus
{
    Created, // Order just placed, waiting for inventory
    InventoryReserved, // Inventory confirmed, waiting for payment
    PaymentProcessed, // Payment successful, waiting for confirmation
    Completed, // All steps done
    Cancelled // Cancelled at any step
}

/// <summary>
/// Represents a line item within an order.
/// </summary>
public class OrderLineItem
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
