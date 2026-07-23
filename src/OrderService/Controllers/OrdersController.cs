using MediatR;
using Microsoft.AspNetCore.Mvc;
using OrderService.Cqrs.Commands;
using OrderService.Cqrs.Queries;
using Shared.Contracts.Dtos;

namespace OrderService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController(IMediator mediator, ILogger<OrdersController> logger) : ControllerBase
{
    private readonly IMediator _mediator = mediator;
    private readonly ILogger<OrdersController> _logger = logger;

    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        _logger.LogInformation(
            "POST /api/orders - Customer: {CustomerId}, Items: {ItemCount}",
            request.CustomerId,
            request.Items.Count);

        var command = new PlaceOrderCommand
        {
            CustomerId = request.CustomerId,
            Items = [.. request.Items.Select(i => new PlaceOrderItemCommand
            {
                ProductId = i.ProductId,
                Quantity = i.Quantity
            })]
        };

        var result = await _mediator.Send(command);

        if (!result.Success)
        {
            _logger.LogWarning("Failed to create order: {Error}", result.ErrorMessage);
            return BadRequest(new { error = result.ErrorMessage });
        }

        _logger.LogInformation("Order {OrderId} created successfully", result.OrderId);

        return CreatedAtAction(
            nameof(GetOrder),
            new { id = result.OrderId },
            new { orderId = result.OrderId, message = "Order placed. Processing will begin shortly." });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetOrder(Guid id)
    {
        _logger.LogInformation("GET /api/orders/{OrderId}", id);

        var query = new GetOrderQuery { OrderId = id };
        var result = await _mediator.Send(query);

        if (result is null)
        {
            return NotFound(new { error = $"Order {id} not found" });
        }

        return Ok(result);
    }

    [HttpGet("customer/{customerId:guid}")]
    public async Task<IActionResult> GetCustomerOrders(Guid customerId)
    {
        _logger.LogInformation("GET /api/orders/customer/{CustomerId}", customerId);

        var query = new GetCustomerOrdersQuery { CustomerId = customerId };
        var result = await _mediator.Send(query);

        return Ok(result);
    }
}
