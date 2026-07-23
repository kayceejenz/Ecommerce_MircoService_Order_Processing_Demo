using MassTransit;
using Shared.Contracts.Events;

namespace NotificationService.Consumers;

public class OrderPlacedConsumer(ILogger<OrderPlacedConsumer> logger) : IConsumer<OrderPlaced>
{
    private readonly ILogger<OrderPlacedConsumer> _logger = logger;

    public Task Consume(ConsumeContext<OrderPlaced> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Order confirmation sent to customer {CustomerId} " +
            "(Order: {OrderId}, Items: {ItemCount}, Total: {TotalAmount})",
            message.CustomerId, message.OrderId, message.Items.Count, message.TotalAmount);

        return Task.CompletedTask;
    }
}
