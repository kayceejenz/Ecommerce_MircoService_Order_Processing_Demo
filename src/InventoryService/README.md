# InventoryService

Event-driven service that manages stock levels. Consumes order events, reserves/releases inventory, and publishes stock status events.

## Purpose

InventoryService is the single source of truth for product stock levels. It participates in the order saga by reserving inventory when orders are placed and releasing it when orders are cancelled. It also detects low-stock conditions and publishes alerts.

## Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                     InventoryService                          │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐  │
│  │              MassTransit Consumers                     │  │
│  │                                                        │  │
│  │  OrderPlacedConsumer ──────► Reserve Stock             │  │
│  │     (IConsumer<OrderPlaced>)    ├── Publish InventoryReserved│
│  │                                ├── Publish InventoryReservationFailed│
│  │                                └── Publish InventoryLow│  │
│  │                                                        │  │
│  │  OrderCancelledConsumer ────► Release Stock            │  │
│  │     (IConsumer<OrderCancelled>)                        │  │
│  └────────────────────────────────────────────────────────┘  │
│                          │                                   │
│                          ▼                                   │
│  ┌────────────────────────────────────────────────────────┐  │
│  │                InventoryController                     │  │
│  │              (REST API for queries)                    │  │
│  └────────────────────────────────────────────────────────┘  │
│                          │                                   │
│                          ▼                                   │
│  ┌────────────────────────────────────────────────────────┐  │
│  │              InventoryDbContext                         │  │
│  │            (PostgreSQL: inventory_db)                   │  │
│  │                                                        │  │
│  │  inventory_items table:                                │  │
│  │  - Id (Product ID)                                     │  │
│  │  - ProductName                                         │  │
│  │  - AvailableQuantity                                   │  │
│  │  - ReservedQuantity                                    │  │
│  │  - ReorderThreshold                                    │  │
│  └────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────┘
         ▲                          │
         │                          ▼
    RabbitMQ                   PostgreSQL
  (OrderPlaced,          (inventory_db)
   OrderCancelled)
```

## How It Works

### Event-Driven Consumer: OrderPlacedConsumer

The primary consumer (`Consumers/OrderPlacedConsumer.cs:15-126`) handles the `OrderPlaced` event from OrderService:

**Step-by-step flow:**

1. Receives `OrderPlaced` event containing order ID, customer ID, and line items
2. For each item in the order:
   a. Looks up `InventoryItem` by product ID
   b. Checks if `AvailableQuantity >= requested Quantity`
   c. If sufficient: decrements `AvailableQuantity`, increments `ReservedQuantity`
   d. If insufficient: adds to failed items list
   e. If stock drops below `ReorderThreshold`: publishes `InventoryLow` event
3. Saves all changes to PostgreSQL in a single `SaveChangesAsync()`
4. If any items failed:
   - Publishes `InventoryReservationFailed` event with detailed failure reason
5. If all items succeeded:
   - Publishes `InventoryReserved` event with list of reserved items

**Stock reservation logic:**
```csharp
// Check availability BEFORE mutation
if (inventoryItem.AvailableQuantity < item.Quantity)
{
    // Insufficient stock - don't mutate, record failure
    failedItems.Add((item.ProductId, item.Quantity, inventoryItem.AvailableQuantity));
    continue;
}

// Mutate only after validation passes
inventoryItem.AvailableQuantity -= item.Quantity;
inventoryItem.ReservedQuantity += item.Quantity;
```

### Event-Driven Consumer: OrderCancelledConsumer

Handles the `OrderCancelled` event (`Consumers/OrderCancelledConsumer.cs:13-51`):

1. Receives `OrderCancelled` event
2. Logs the cancellation with reason
3. In production: would look up which items were reserved for this order and release them back to available stock

**Note:** This is a simplified implementation. A production system would maintain an `OrderInventoryReservations` table mapping `OrderId -> List<ProductId, Quantity>` for precise stock release.

### REST API

The `InventoryController` (`Controllers/InventoryController.cs`) provides query endpoints:

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/inventory` | List all inventory items |
| GET | `/api/inventory/{productId}` | Get stock for specific product |
| GET | `/api/inventory/low-stock` | List items below reorder threshold |
| POST | `/api/inventory` | Create new inventory record |
| PUT | `/api/inventory/{productId}` | Update stock levels |

### Seed Data

On startup, 5 inventory items are seeded (matching CatalogService products):

| Product | Stock | Reorder Threshold |
|---------|-------|-------------------|
| Wireless Mouse | 150 | 10 |
| Mechanical Keyboard | 75 | 10 |
| USB-C Hub | 200 | 15 |
| Cotton T-Shirt | 500 | 25 |
| Running Shoes | 100 | 10 |

## Key Files

| File | Purpose |
|------|---------|
| `Program.cs` | Entry point. Configures EF Core, MassTransit consumers, OpenTelemetry, Swagger, seed data |
| `Consumers/OrderPlacedConsumer.cs` | Primary consumer. Reserves stock, publishes success/failure/low-stock events |
| `Consumers/OrderCancelledConsumer.cs` | Handles order cancellations (releases reserved stock) |
| `Controllers/InventoryController.cs` | REST API for inventory queries |
| `Data/InventoryDbContext.cs` | EF Core DbContext for PostgreSQL |
| `Data/Entities/InventoryItem.cs` | Entity: Id, ProductName, AvailableQuantity, ReservedQuantity, ReorderThreshold |
| `appsettings.json` | Connection strings, RabbitMQ config, Serilog config |

## Database Schema

### Inventory Items Table

```sql
CREATE TABLE inventory_items (
    Id                 UUID PRIMARY KEY,
    ProductName        VARCHAR(200) NOT NULL,
    AvailableQuantity  INTEGER DEFAULT 0,
    ReservedQuantity   INTEGER DEFAULT 0,
    ReorderThreshold   INTEGER DEFAULT 10,
    CreatedAt          TIMESTAMP,
    UpdatedAt          TIMESTAMP
);

CREATE INDEX idx_inventory_product_name ON inventory_items(ProductName);
```

## Event Publishing

When processing an `OrderPlaced` event, the service publishes one of three events:

### InventoryReserved (Success)
```json
{
  "orderId": "guid",
  "customerId": "guid",
  "reservedItems": [
    { "productId": "guid", "quantity": 2 }
  ],
  "reservedAt": "2024-01-01T00:00:00Z"
}
```

### InventoryReservationFailed (Insufficient Stock)
```json
{
  "orderId": "guid",
  "customerId": "guid",
  "reason": "Insufficient stock for product X: requested 10, available 3",
  "failedAt": "2024-01-01T00:00:00Z"
}
```

### InventoryLow (Below Threshold)
```json
{
  "productId": "guid",
  "productName": "Wireless Mouse",
  "currentQuantity": 5,
  "reorderThreshold": 10,
  "detectedAt": "2024-01-01T00:00:00Z"
}
```

## Configuration and Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `ConnectionStrings__InventoryDb` | `Host=localhost;Database=inventory_db;...` | PostgreSQL connection |
| `RabbitMQ__Host` | `localhost` | RabbitMQ host |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `http://localhost:4317` | OpenTelemetry endpoint |

**Docker Compose overrides:**
```yaml
environment:
  - ConnectionStrings__InventoryDb=Host=postgres;Database=inventory_db;Username=postgres;Password=postgres
  - RabbitMQ__Host=rabbitmq
```

## How to Test

### Start the Service

```bash
dotnet run --project src/InventoryService
```

Runs on `http://localhost:5030`.

### Health Check

```bash
curl http://localhost:5030/health
```

### Query Inventory

```bash
# List all inventory
curl http://localhost:5030/api/inventory

# Get specific product stock
curl http://localhost:5030/api/inventory/{product-id}

# List low-stock items
curl http://localhost:5030/api/inventory/low-stock
```

### Create Inventory Record

```bash
curl -X POST http://localhost:5030/api/inventory \
  -H "Content-Type: application/json" \
  -d '{"productId": "...", "productName": "Widget", "initialQuantity": 100, "reorderThreshold": 15}'
```

### Test Event Consumption

Publish an `OrderPlaced` event to RabbitMQ (via management UI at `http://localhost:15672`) and watch the consumer process it. Check logs for:
- Stock reservation success/failure
- `InventoryReserved` or `InventoryReservationFailed` event published
- `InventoryLow` event if stock dropped below threshold

### Test Low Stock Detection

```bash
# Update stock to below threshold
curl -X PUT http://localhost:5030/api/inventory/{product-id} \
  -H "Content-Type: application/json" \
  -d '{"availableQuantity": 3}'
```

Then trigger an order for that product and observe the `InventoryLow` event in logs.

## Communication Patterns Demonstrated

| Pattern | Implementation |
|---------|---------------|
| **Event-Driven Consumer** | MassTransit `IConsumer<T>` for OrderPlaced and OrderCancelled |
| **Event Publishing** | Publishes InventoryReserved, InventoryReservationFailed, InventoryLow |
| **Stock Reservation** | Check-then-mutate pattern with availability validation |
| **REST API** | Query endpoints for inventory status |
| **Database Per Service** | Dedicated `inventory_db` PostgreSQL database |
| **OpenTelemetry** | Distributed tracing with OTLP export |

## Dependencies

- **MassTransit** - Message bus consumer framework
- **Entity Framework Core** - PostgreSQL ORM
- **Npgsql** - PostgreSQL driver
- **Serilog** - Structured logging
- **OpenTelemetry** - Distributed tracing
