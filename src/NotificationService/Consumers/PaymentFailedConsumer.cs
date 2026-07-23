using MassTransit;
using Shared.Contracts.Events;

namespace NotificationService.Consumers;

public class PaymentFailedConsumer(ILogger<PaymentFailedConsumer> logger) : IConsumer<PaymentFailed>
{
    private readonly ILogger<PaymentFailedConsumer> _logger = logger;

    public Task Consume(ConsumeContext<PaymentFailed> context)
    {
        var message = context.Message;

        _logger.LogWarning(
            "Payment failure notification sent to customer {CustomerId} " +
            "(Order: {OrderId}, Amount: {Amount}, Reason: {Reason})",
            message.CustomerId, message.OrderId, message.Amount, message.Reason);

        return Task.CompletedTask;
    }
}
