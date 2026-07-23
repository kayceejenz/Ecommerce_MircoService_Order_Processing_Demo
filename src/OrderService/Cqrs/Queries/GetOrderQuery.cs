// =============================================================================
// GetOrderQuery - CQRS Read Side Query
// =============================================================================
// WHAT: Represents a request to read order data.
//      In CQRS, queries are operations that READ state without changing it.
//
// WHY SEPARATE FROM COMMANDS:
//   - Commands go through EventStoreDB (write model)
//   - Queries go through PostgreSQL (read model)
//   - Different optimization strategies:
//     - Write: Optimized for appending events (sequential I/O)
//     - Read: Optimized for fast queries (indexed, denormalized)
// =============================================================================

using MediatR;

namespace OrderService.Cqrs.Queries;

/// <summary>
/// Query to get a single order by ID.
/// Returns the order from the read model (PostgreSQL).
/// </summary>
public record GetOrderQuery : IRequest<Shared.Contracts.Dtos.OrderDto?>
{
    public Guid OrderId { get; init; }
}

/// <summary>
/// Query to get all orders for a customer.
/// </summary>
public record GetCustomerOrdersQuery : IRequest<List<Shared.Contracts.Dtos.OrderDto>>
{
    public Guid CustomerId { get; init; }
}
