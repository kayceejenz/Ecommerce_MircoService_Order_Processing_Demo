using MassTransit;
using Shared.Contracts.Events;

namespace NotificationService.Consumers;

public class InventoryLowConsumer(ILogger<InventoryLowConsumer> logger) : IConsumer<InventoryLow>
{
    private readonly ILogger<InventoryLowConsumer> _logger = logger;

    public Task Consume(ConsumeContext<InventoryLow> context)
    {
        var message = context.Message;

        _logger.LogWarning(
            "Low stock alert: {ProductName} has only {CurrentQuantity} units " +
            "(Threshold: {ReorderThreshold}, Detected: {DetectedAt})",
            message.ProductName, message.CurrentQuantity, message.ReorderThreshold, message.DetectedAt);

        return Task.CompletedTask;
    }
}
