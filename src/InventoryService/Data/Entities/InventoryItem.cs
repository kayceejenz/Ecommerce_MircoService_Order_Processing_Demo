namespace InventoryService.Data.Entities;

/// <summary>
/// Represents stock levels for a product.
/// Maps to the "inventory_items" table via EF Core conventions.
/// </summary>
public class InventoryItem
{
    /// <summary>
    /// Primary key. Matches the ProductId from CatalogService
    /// since each product has exactly one inventory record.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>Product display name (denormalized for fast lookups)</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>
    /// Current available stock. Decremented when orders are reserved,
    /// incremented when reservations are cancelled.
    /// </summary>
    public int AvailableQuantity { get; set; }

    /// <summary>
    /// Stock reserved for pending orders. Incremented on reservation,
    /// decremented on confirmation or cancellation.
    /// </summary>
    public int ReservedQuantity { get; set; }

    /// <summary>
    /// Reorder threshold. When AvailableQuantity drops below this value,
    /// an InventoryLow event is published.
    /// </summary>
    public int ReorderThreshold { get; set; } = 10;

    /// <summary>When the inventory record was created</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the inventory record was last updated</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
