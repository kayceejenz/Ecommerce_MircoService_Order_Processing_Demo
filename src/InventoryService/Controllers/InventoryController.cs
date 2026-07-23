using InventoryService.Data;
using InventoryService.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Dtos;

namespace InventoryService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InventoryController(InventoryDbContext db, ILogger<InventoryController> logger) : ControllerBase
{
    private readonly InventoryDbContext _db = db;
    private readonly ILogger<InventoryController> _logger = logger;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        _logger.LogInformation("GET /api/inventory");

        var items = await _db.InventoryItems
            .OrderBy(i => i.ProductName)
            .Select(i => new InventoryDto
            {
                ProductId = i.Id,
                ProductName = i.ProductName,
                AvailableQuantity = i.AvailableQuantity,
                ReservedQuantity = i.ReservedQuantity,
                ReorderThreshold = i.ReorderThreshold
            })
            .ToListAsync();

        return Ok(items);
    }

    [HttpGet("{productId:guid}")]
    public async Task<IActionResult> GetByProductId(Guid productId)
    {
        _logger.LogInformation("GET /api/inventory/{ProductId}", productId);

        var item = await _db.InventoryItems.FindAsync(productId);
        if (item is null)
            return NotFound(new { error = $"Inventory item for product {productId} not found" });

        var dto = new InventoryDto
        {
            ProductId = item.Id,
            ProductName = item.ProductName,
            AvailableQuantity = item.AvailableQuantity,
            ReservedQuantity = item.ReservedQuantity,
            ReorderThreshold = item.ReorderThreshold
        };

        return Ok(dto);
    }

    [HttpGet("low-stock")]
    public async Task<IActionResult> GetLowStock()
    {
        _logger.LogInformation("GET /api/inventory/low-stock");

        var items = await _db.InventoryItems
            .Where(i => i.AvailableQuantity < i.ReorderThreshold)
            .OrderBy(i => i.AvailableQuantity)
            .Select(i => new InventoryDto
            {
                ProductId = i.Id,
                ProductName = i.ProductName,
                AvailableQuantity = i.AvailableQuantity,
                ReservedQuantity = i.ReservedQuantity,
                ReorderThreshold = i.ReorderThreshold
            })
            .ToListAsync();

        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateInventoryRequest request)
    {
        _logger.LogInformation("POST /api/inventory - ProductId: {ProductId}, Quantity: {Quantity}",
            request.ProductId, request.InitialQuantity);

        var existing = await _db.InventoryItems.FindAsync(request.ProductId);
        if (existing is not null)
            return Conflict(new { error = $"Inventory item for product {request.ProductId} already exists" });

        var item = new InventoryItem
        {
            Id = request.ProductId,
            ProductName = request.ProductName,
            AvailableQuantity = request.InitialQuantity,
            ReservedQuantity = 0,
            ReorderThreshold = request.ReorderThreshold,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.InventoryItems.Add(item);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Created inventory item for Product {ProductId}", request.ProductId);

        return CreatedAtAction(nameof(GetByProductId), new { productId = item.Id }, new InventoryDto
        {
            ProductId = item.Id,
            ProductName = item.ProductName,
            AvailableQuantity = item.AvailableQuantity,
            ReservedQuantity = item.ReservedQuantity,
            ReorderThreshold = item.ReorderThreshold
        });
    }

    [HttpPut("{productId:guid}")]
    public async Task<IActionResult> Update(Guid productId, [FromBody] UpdateInventoryRequest request)
    {
        _logger.LogInformation("PUT /api/inventory/{ProductId}", productId);

        var item = await _db.InventoryItems.FindAsync(productId);
        if (item is null)
            return NotFound(new { error = $"Inventory item for product {productId} not found" });

        if (request.ProductName is not null) item.ProductName = request.ProductName;
        if (request.AvailableQuantity.HasValue) item.AvailableQuantity = request.AvailableQuantity.Value;
        if (request.ReorderThreshold.HasValue) item.ReorderThreshold = request.ReorderThreshold.Value;
        item.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _logger.LogInformation("Updated inventory item for Product {ProductId}", productId);

        return Ok(new InventoryDto
        {
            ProductId = item.Id,
            ProductName = item.ProductName,
            AvailableQuantity = item.AvailableQuantity,
            ReservedQuantity = item.ReservedQuantity,
            ReorderThreshold = item.ReorderThreshold
        });
    }
}

#region dto
public record CreateInventoryRequest
{
    public Guid ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public int InitialQuantity { get; init; }
    public int ReorderThreshold { get; init; } = 10;
}

public record UpdateInventoryRequest
{
    public string? ProductName { get; init; }
    public int? AvailableQuantity { get; init; }
    public int? ReorderThreshold { get; init; }
}
#endregion