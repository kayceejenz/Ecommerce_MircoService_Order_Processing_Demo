using MediatR;
using Microsoft.EntityFrameworkCore;
using OrderService.ReadModel;
using Shared.Contracts.Dtos;

namespace OrderService.Cqrs.Queries;

/// <summary>
/// Handler for GetOrderQuery.
/// Reads from the denormalized PostgreSQL read model.
/// </summary>
public class GetOrderHandler(OrderReadDbContext readDb, ILogger<GetOrderHandler> logger) :
    IRequestHandler<GetOrderQuery, OrderDto?>,
    IRequestHandler<GetCustomerOrdersQuery, List<OrderDto>>
{
    private readonly OrderReadDbContext _readDb = readDb;
    private readonly ILogger<GetOrderHandler> _logger = logger;

    /// <summary>
    /// Get a single order by ID from the read model.
    /// </summary>
    public async Task<OrderDto?> Handle(GetOrderQuery request, CancellationToken ct)
    {
        _logger.LogDebug("Querying order {OrderId} from read model", request.OrderId);

        var readModel = await _readDb.Orders
            .AsNoTracking()  // Don't track changes - read-only query
            .FirstOrDefaultAsync(o => o.OrderId == request.OrderId, ct);

        if (readModel is null)
        {
            _logger.LogDebug("Order {OrderId} not found in read model", request.OrderId);
            return null;
        }

        return MapToDto(readModel);
    }

    /// <summary>
    /// Get all orders for a customer.
    /// </summary>
    public async Task<List<OrderDto>> Handle(GetCustomerOrdersQuery request, CancellationToken ct)
    {
        _logger.LogDebug("Querying orders for customer {CustomerId}", request.CustomerId);

        var readModels = await _readDb.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == request.CustomerId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);

        return readModels.Select(MapToDto).ToList();
    }

    /// <summary>
    /// Map the read model to the API DTO.
    /// This is where denormalization happens - all data is in one object.
    /// </summary>
    private static OrderDto MapToDto(OrderReadModel readModel)
    {
        var items = System.Text.Json.JsonSerializer.Deserialize<List<OrderItemDto>>(readModel.ItemsJson) ?? new();

        return new OrderDto
        {
            OrderId = readModel.OrderId,
            CustomerId = readModel.CustomerId,
            Items = items,
            TotalAmount = readModel.TotalAmount,
            Status = readModel.Status,
            CreatedAt = readModel.CreatedAt,
            CompletedAt = readModel.CompletedAt
        };
    }
}
