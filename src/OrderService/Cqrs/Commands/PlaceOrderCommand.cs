using MediatR;

namespace OrderService.Cqrs.Commands;

/// <summary>
/// Command to place a new order.
/// Returns the created order ID on success.
/// </summary>
public record PlaceOrderCommand : IRequest<PlaceOrderResult>
{
    public Guid CustomerId { get; init; }
    public List<PlaceOrderItemCommand> Items { get; init; } = new();
}

public record PlaceOrderItemCommand
{
    public Guid ProductId { get; init; }
    public int Quantity { get; init; }
}

public record PlaceOrderResult
{
    public Guid OrderId { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}
