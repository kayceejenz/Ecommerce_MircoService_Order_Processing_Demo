using MediatR;
using OrderService.Domain;
using OrderService.EventStore;
using OrderService.ReadModel;
using System.Text.Json;

namespace OrderService.Cqrs.Commands;

/// <summary>
/// Handler for PlaceOrderCommand.
/// Orchestrates order creation using event sourcing.
/// </summary>
public class PlaceOrderHandler(
    EventStoreRepository eventStore,
    OrderReadDbContext readDb,
    ILogger<PlaceOrderHandler> logger) : IRequestHandler<PlaceOrderCommand, PlaceOrderResult>
{
    private readonly EventStoreRepository _eventStore = eventStore;
    private readonly OrderReadDbContext _readDb = readDb;
    private readonly ILogger<PlaceOrderHandler> _logger = logger;

    public async Task<PlaceOrderResult> Handle(PlaceOrderCommand request, CancellationToken ct)
    {
        _logger.LogInformation(
            "Processing PlaceOrderCommand for customer {CustomerId} with {ItemCount} items",
            request.CustomerId,
            request.Items.Count);

        try
        {
           // Validate input
            if (request.Items.Count == 0)
            {
                return new PlaceOrderResult
                {
                    Success = false,
                    ErrorMessage = "Order must contain at least one item"
                };
            }

            // Create Order aggregate (this raises OrderPlaced event)
            var order = Order.Create(
                request.CustomerId,
                request.Items.Select(i => (i.ProductId, i.Quantity, GetUnitPrice())).ToList());

            // Save events to EventStoreDB
            await _eventStore.SaveAsync(order, ct);

            // Update read model in PostgreSQL (projection)
            var readModel = new OrderReadModel
            {
                OrderId = order.Id,
                CustomerId = order.CustomerId,
                Status = order.Status.ToString(),
                TotalAmount = order.TotalAmount,
                ItemsJson = JsonSerializer.Serialize(order.Items),
                CreatedAt = order.CreatedAt,
                LastEventSequence = 0
            };

            _readDb.Orders.Add(readModel);
            await _readDb.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Order {OrderId} created successfully. Total: ${TotalAmount}",
                order.Id,
                order.TotalAmount);

            return new PlaceOrderResult
            {
                OrderId = order.Id,
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create order for customer {CustomerId}", request.CustomerId);
            return new PlaceOrderResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// In a real system, this would look up the product price from CatalogService.
    /// For demo purposes, we use a fixed price.
    /// </summary>
    private static decimal GetUnitPrice() => 29.99m;
}
