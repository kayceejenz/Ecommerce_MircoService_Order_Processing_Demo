namespace PaymentService.Commands;

/// <summary>
/// Command sent by the OrderSaga to process a payment.
/// </summary>
public record ProcessPaymentCommand
{
    public Guid OrderId { get; init; }
    public Guid CustomerId { get; init; }
    public decimal Amount { get; init; }
}
