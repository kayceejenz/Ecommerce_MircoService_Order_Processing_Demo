using Microsoft.AspNetCore.Mvc;
using ApiGateway.Clients;
using Shared.Contracts.Dtos;

namespace ApiGateway.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GatewayController(
    CatalogClient catalog,
    OrderClient order,
    PaymentClient payment,
    ILogger<GatewayController> logger) : ControllerBase
{
    private readonly CatalogClient _catalog = catalog;
    private readonly OrderClient _order = order;
    private readonly PaymentClient _payment = payment;
    private readonly ILogger<GatewayController> _logger = logger;

    #region Products
    [HttpGet("products")]
    public async Task<IActionResult> GetProducts(CancellationToken ct)
    {
        _logger.LogInformation("Gateway: GetProducts");
        var products = await _catalog.GetProductsAsync(ct);
        return Ok(products);
    }

    [HttpGet("products/{id:guid}")]
    public async Task<IActionResult> GetProduct(Guid id, CancellationToken ct)
    {
        _logger.LogInformation("Gateway: GetProduct {ProductId}", id);
        var product = await _catalog.GetProductByIdAsync(id, ct);
        return product is null ? NotFound() : Ok(product);
    }
    #endregion

    #region Inventory

    [HttpGet("inventory")]
    public async Task<IActionResult> GetInventory(CancellationToken ct)
    {
        _logger.LogInformation("Gateway: GetInventory");
        var inventory = await _catalog.GetInventoryAsync(ct);
        return Ok(inventory);
    }

    [HttpGet("inventory/{productId:guid}")]
    public async Task<IActionResult> GetInventoryByProduct(Guid productId, CancellationToken ct)
    {
        _logger.LogInformation("Gateway: GetInventoryByProduct {ProductId}", productId);
        var item = await _catalog.GetInventoryByProductAsync(productId, ct);
        return item is null ? NotFound() : Ok(item);
    }
    #endregion

    #region Orders
    [HttpPost("orders")]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request, CancellationToken ct)
    {
        _logger.LogInformation("Gateway: CreateOrder for customer {CustomerId}", request.CustomerId);
        var order = await _order.CreateOrderAsync(request, ct);
        return CreatedAtAction(nameof(GetOrder), new { id = order.OrderId }, order);
    }

    [HttpGet("orders/{id:guid}")]
    public async Task<IActionResult> GetOrder(Guid id, CancellationToken ct)
    {
        _logger.LogInformation("Gateway: GetOrder {OrderId}", id);
        var order = await _order.GetOrderByIdAsync(id, ct);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpGet("orders/customer/{customerId:guid}")]
    public async Task<IActionResult> GetOrdersByCustomer(Guid customerId, CancellationToken ct)
    {
        _logger.LogInformation("Gateway: GetOrdersByCustomer {CustomerId}", customerId);
        var orders = await _order.GetOrdersByCustomerAsync(customerId, ct);
        return Ok(orders);
    }

    #endregion

    #region Payments

    [HttpGet("payments/order/{orderId:guid}")]
    public async Task<IActionResult> GetPaymentStatus(Guid orderId, CancellationToken ct)
    {
        _logger.LogInformation("Gateway: GetPaymentStatus for order {OrderId}", orderId);
        var payment = await _payment.GetPaymentStatusAsync(orderId, ct);
        return payment is null ? NotFound() : Ok(payment);
    }

    [HttpPost("payments")]
    public async Task<IActionResult> ProcessPayment([FromBody] ProcessPaymentRequest request, CancellationToken ct)
    {
        _logger.LogInformation("Gateway: ProcessPayment for order {OrderId}", request.OrderId);
        var result = await _payment.ProcessPaymentAsync(request.OrderId, request.Amount, ct);
        return Ok(result);
    }
    #endregion
}

#region dto
public record ProcessPaymentRequest
{
    public Guid OrderId { get; init; }
    public decimal Amount { get; init; }
}
#endregion