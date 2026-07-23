using InventoryService.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Events;
using Shared.Contracts.Dtos;

namespace InventoryService.Consumers;

/// <summary>
/// Consumes OrderPlaced events and attempts to reserve inventory
/// for each line item. Publishes InventoryReserved on success or
/// InventoryReservationFailed if any item has insufficient stock.
/// Also publishes InventoryLow when stock drops below the reorder threshold.
/// </summary>
public class OrderPlacedConsumer : IConsumer<OrderPlaced>
{
    private readonly InventoryDbContext _db;
    private readonly ILogger<OrderPlacedConsumer> _logger;

    public OrderPlacedConsumer(InventoryDbContext db, ILogger<OrderPlacedConsumer> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OrderPlaced> context)
    {
        var message = context.Message;
        _logger.LogInformation(
            "Processing OrderPlaced for OrderId: {OrderId}, Items: {ItemCount}",
            message.OrderId, message.Items.Count);

        var reservedItems = new List<ReservedItem>();
        var failedItems = new List<(Guid ProductId, int Requested, int Available)>();

        foreach (var item in message.Items)
        {
            var inventoryItem = await _db.InventoryItems
                .FirstOrDefaultAsync(i => i.Id == item.ProductId);

            if (inventoryItem is null)
            {
                _logger.LogWarning(
                    "Product {ProductId} not found in inventory for OrderId: {OrderId}",
                    item.ProductId, message.OrderId);
                failedItems.Add((item.ProductId, item.Quantity, 0));
                continue;
            }

            if (inventoryItem.AvailableQuantity < item.Quantity)
            {
                _logger.LogWarning(
                    "Insufficient stock for Product {ProductId}: requested {Requested}, available {Available}",
                    item.ProductId, item.Quantity, inventoryItem.AvailableQuantity);
                failedItems.Add((item.ProductId, item.Quantity, inventoryItem.AvailableQuantity));
                continue;
            }

            // Reserve stock
            inventoryItem.AvailableQuantity -= item.Quantity;
            inventoryItem.ReservedQuantity += item.Quantity;
            inventoryItem.UpdatedAt = DateTime.UtcNow;

            reservedItems.Add(new ReservedItem
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity
            });

            _logger.LogInformation(
                "Reserved {Quantity} of Product {ProductId} for Order {OrderId}. " +
                "Available: {Available}, Reserved: {Reserved}",
                item.Quantity, item.ProductId, message.OrderId,
                inventoryItem.AvailableQuantity, inventoryItem.ReservedQuantity);

            // Check if stock is now below reorder threshold
            if (inventoryItem.AvailableQuantity < inventoryItem.ReorderThreshold)
            {
                _logger.LogWarning(
                    "Low stock detected for Product {ProductId}: {Available} remaining (threshold: {Threshold})",
                    item.ProductId, inventoryItem.AvailableQuantity, inventoryItem.ReorderThreshold);

                await context.Publish<InventoryLow>(new
                {
                    ProductId = inventoryItem.Id,
                    ProductName = inventoryItem.ProductName,
                    CurrentQuantity = inventoryItem.AvailableQuantity,
                    ReorderThreshold = inventoryItem.ReorderThreshold,
                    DetectedAt = DateTime.UtcNow
                });
            }
        }

        await _db.SaveChangesAsync();

        if (failedItems.Count > 0)
        {
            var reason = string.Join("; ", failedItems.Select(f =>
                $"Insufficient stock for product {f.ProductId}: requested {f.Requested}, available {f.Available}"));

            _logger.LogWarning(
                "Reservation failed for Order {OrderId}: {Reason}", message.OrderId, reason);

            await context.Publish<InventoryReservationFailed>(new
            {
                OrderId = message.OrderId,
                CustomerId = message.CustomerId,
                Reason = reason,
                FailedAt = DateTime.UtcNow
            });
        }
        else
        {
            _logger.LogInformation(
                "All items reserved successfully for Order {OrderId}", message.OrderId);

            await context.Publish<InventoryReserved>(new
            {
                OrderId = message.OrderId,
                CustomerId = message.CustomerId,
                ReservedItems = reservedItems,
                ReservedAt = DateTime.UtcNow
            });
        }
    }
}
