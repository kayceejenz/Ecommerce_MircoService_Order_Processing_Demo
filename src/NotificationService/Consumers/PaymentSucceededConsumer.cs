using MassTransit;
using Shared.Contracts.Events;

namespace NotificationService.Consumers;

public class PaymentSucceededConsumer(ILogger<PaymentSucceededConsumer> logger) : IConsumer<PaymentSucceeded>
{
    private readonly ILogger<PaymentSucceededConsumer> _logger = logger;

    public Task Consume(ConsumeContext<PaymentSucceeded> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Payment confirmation sent to customer {CustomerId} " +
            "(Order: {OrderId}, Amount: {Amount}, Transaction: {TransactionId})",
            message.CustomerId, message.OrderId, message.Amount, message.TransactionId);

        return Task.CompletedTask;
    }
}
