using MassTransit;
using PaymentService.Commands;
using Shared.Contracts.Events;

namespace PaymentService.Consumers;

public class ProcessPaymentConsumer(ILogger<ProcessPaymentConsumer> logger) : IConsumer<ProcessPaymentCommand>
{
    private readonly ILogger<ProcessPaymentConsumer> _logger = logger;
    private static readonly Random _random = new();

    public async Task Consume(ConsumeContext<ProcessPaymentCommand> context)
    {
        var command = context.Message;
        _logger.LogInformation(
            "Processing payment for Order {OrderId}, Customer {CustomerId}, Amount {Amount:C}",
            command.OrderId, command.CustomerId, command.Amount);

        // Simulate payment processing delay
        await Task.Delay(500, context.CancellationToken);

        // Simulate 90% success rate
        var success = _random.Next(100) < 90;

        if (success)
        {
            var transactionId = $"TXN-{Guid.NewGuid():N}".Substring(0, 20);

            _logger.LogInformation(
                "Payment succeeded for Order {OrderId}. Transaction: {TransactionId}",
                command.OrderId, transactionId);

            await context.Publish(new PaymentSucceeded
            {
                OrderId = command.OrderId,
                CustomerId = command.CustomerId,
                Amount = command.Amount,
                TransactionId = transactionId,
                ProcessedAt = DateTime.UtcNow
            });
        }
        else
        {
            var reason = "Payment declined by provider";

            _logger.LogWarning(
                "Payment failed for Order {OrderId}. Reason: {Reason}",
                command.OrderId, reason);

            await context.Publish(new PaymentFailed
            {
                OrderId = command.OrderId,
                CustomerId = command.CustomerId,
                Amount = command.Amount,
                Reason = reason,
                FailedAt = DateTime.UtcNow
            });
        }
    }
}
