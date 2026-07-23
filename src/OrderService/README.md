# OrderService

The most complex service in the system. Demonstrates CQRS, Event Sourcing, and Saga orchestration for distributed order processing.

## Purpose

OrderService is the core domain service that manages the order lifecycle. It coordinates a distributed transaction across multiple services (inventory reservation, payment processing) using the Saga pattern, while maintaining a complete audit trail through Event Sourcing and providing fast reads through CQRS.

## Architecture

```
┌──────────────────────────────────────────────────────────────────────┐
│                          OrderService                                 │
│                                                                      │
│  ┌─────────────────────────────────────────────────────────────────┐  │
│  │                     REST API Layer                              │  │
│  │  OrdersController  ─────────────────────────────────────────►  │  │
│  └──────────────┬────────────────────────────────────────────────┘  │
│                 │                                                    │
│                 ▼                                                    │
│  ┌─────────────────────────────────────────────────────────────────┐  │
│  │                    CQRS Layer (MediatR)                         │  │
│  │                                                                 │  │
│  │  WRITE SIDE:                      READ SIDE:                    │  │
│  │  PlaceOrderCommand                GetOrderQuery                 │  │
│  │        │                                 │                      │  │
│  │        ▼                                 ▼                      │  │
│  │  PlaceOrderHandler               GetOrderHandler                │  │
│  │        │                                 │                      │  │
│  │        ▼                                 ▼                      │  │
│  │  Order Aggregate              PostgreSQL Read Model             │  │
│  │  (Event Sourcing)             (Denormalized DTOs)               │  │
│  │        │                                                    │  │
│  │        ▼                                                    │  │
│  │  EventStoreDB                                                    │  │
│  │  (Immutable Events)                                              │  │
│  └─────────────────────────────────────────────────────────────────┘  │
│                 │                                                    │
│                 ▼                                                    │
│  ┌─────────────────────────────────────────────────────────────────┐  │
│  │              Saga Orchestrator (MassTransit)                    │  │
│  │                                                                 │  │
│  │  OrderStateMachine  ─────►  OrderSagaState                     │  │
│  │  (State machine logic)        (Persisted in PostgreSQL)         │  │
│  └─────────────────────────────────────────────────────────────────┘  │
│                 │                                                    │
│                 ▼                                                    │
│  ┌─────────────────────────────────────────────────────────────────┐  │
│  │                    gRPC Server                                  │  │
│  │  OrderGrpcServiceImpl  (for internal service-to-service calls)  │  │
│  └─────────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────────┘
         │                            │
         ▼                            ▼
  ┌──────────────┐           ┌──────────────────┐
  │ EventStoreDB │           │ PostgreSQL (x2)   │
  │ (Events)     │           │ order_read_db     │
  └──────────────┘           │ saga_state_db     │
                             └──────────────────┘
```

## How It Works

### CQRS (Command Query Responsibility Segregation)

CQRS separates read and write operations into different models:

**Write Side (Commands)** - Changes state:
```
HTTP POST /api/orders
    → OrdersController
    → IMediator.Send(PlaceOrderCommand)
    → PlaceOrderHandler
    → Order.Create() raises OrderPlaced event
    → EventStoreRepository.SaveAsync() persists events
    → OrderReadDbContext updates read model
```

**Read Side (Queries)** - Reads state:
```
HTTP GET /api/orders/{id}
    → OrdersController
    → IMediator.Send(GetOrderQuery)
    → GetOrderHandler
    → Queries PostgreSQL OrderReadDbContext
    → Returns OrderDto
```

### Event Sourcing

Instead of storing the current state in a database row, OrderService stores a sequence of **immutable events**:

```
Traditional DB:              Event Sourcing:
┌─────────────────┐          ┌──────────────────────────────────┐
│ Order           │          │ Events:                           │
│ Id: 123         │          │  1. OrderPlaced (items, total)   │
│ Status: Paid    │          │  2. InventoryReserved            │
│ Total: $50      │          │  3. PaymentProcessed ($50)       │
└─────────────────┘          │  4. OrderConfirmed               │
                             └──────────────────────────────────┘
```

**Order Aggregate** (`Domain/Order.cs`):
- Factory method `Order.Create()` validates business rules and raises `OrderPlaced` event
- Business operations (`ConfirmInventoryReserved()`, `ConfirmPayment()`, `Complete()`, `Cancel()`) enforce state machine transitions
- `Replay()` method rebuilds state from historical events
- `_uncommittedEvents` tracks events not yet saved to EventStoreDB

**EventStoreRepository** (`EventStore/EventStoreRepository.cs`):
- `SaveAsync()` appends uncommitted events to an EventStoreDB stream (`order-{orderId}`)
- `LoadAsync()` reads all events from a stream and replays them to rebuild current state
- Supports `OrderPlaced` and `OrderCancelled` event types

### Order Status Lifecycle

```
Created ──────► InventoryReserved ──────► PaymentProcessed ──────► Completed
   │                    │                       │
   │                    │                       │
   ▼                    ▼                       ▼
Cancelled           Cancelled              Cancelled
```

### Saga State Machine

The `OrderStateMachine` (`Sagas/OrderStateMachine.cs`) orchestrates the distributed workflow:

**States:**
- `Started` - Order placed, waiting for inventory
- `InventoryReserved` - Inventory confirmed, waiting for payment
- `Completed` - All steps succeeded
- `Failed` - Compensation in progress or completed

**Events triggering transitions:**

| Event | From State | To State | Action |
|-------|-----------|----------|--------|
| `OrderPlaced` | Initial | Started | Send `ReserveInventoryCommand` |
| `InventoryReserved` | Started | InventoryReserved | Send `ProcessPaymentCommand` |
| `InventoryReservationFailed` | Started | Failed | Publish `OrderCancelled` |
| `PaymentSucceeded` | InventoryReserved | Completed | Saga complete |
| `PaymentFailed` | InventoryReserved | Failed | Send `ReleaseInventoryCommand`, publish `OrderCancelled` |

**Message Flow - Happy Path:**

```
OrderService                InventoryService           PaymentService
    │                              │                         │
    │── OrderPlaced event ────────►│                         │
    │   (via RabbitMQ)             │                         │
    │                              │── ReserveInventory ────►│
    │                              │   (command)             │
    │◄── InventoryReserved ────────│                         │
    │   (event)                    │                         │
    │── ProcessPayment ─────────────────────────────────────►│
    │   (command)                  │                         │
    │◄── PaymentSucceeded ──────────────────────────────────│
    │   (event)                    │                         │
    │   Saga → Completed           │                         │
```

**Message Flow - Payment Failure:**

```
OrderService                InventoryService           PaymentService
    │                              │                         │
    │◄── InventoryReserved ────────│                         │
    │                              │                         │
    │── ProcessPayment ─────────────────────────────────────►│
    │                              │                         │
    │◄── PaymentFailed ─────────────────────────────────────│
    │                              │                         │
    │── ReleaseInventory ─────────►│                         │
    │   (command)                  │                         │
    │── OrderCancelled ─────────────────────────────────────►│
    │   (event, for notification)  │                         │
    │   Saga → Failed              │                         │
```

### gRPC Server

`OrderGrpcServiceImpl` exposes gRPC endpoints for internal service-to-service calls:

```protobuf
service OrderGrpcService {
  rpc CreateOrder (CreateOrderGrpcRequest) returns (CreateOrderGrpcResponse);
  rpc GetOrder (GetOrderGrpcRequest) returns (OrderGrpcResponse);
}
```

Converts between gRPC protobuf messages and MediatR commands/queries, providing binary-protocol performance for internal calls.

## Key Files

| File | Purpose |
|------|---------|
| `Program.cs` | Entry point. Configures MediatR, EventStoreDB, PostgreSQL (read model + saga state), MassTransit + RabbitMQ, gRPC, OpenTelemetry |
| **CQRS** | |
| `Cqrs/Commands/PlaceOrderCommand.cs` | Write-side command definition. `IRequest<PlaceOrderResult>` |
| `Cqrs/Commands/PlaceOrderHandler.cs` | Command handler. Creates Order aggregate, saves to EventStoreDB, updates read model |
| `Cqrs/Queries/GetOrderQuery.cs` | Read-side query definitions |
| `Cqrs/Queries/GetOrderHandler.cs` | Query handler. Reads from PostgreSQL read model, maps to DTOs |
| **Domain** | |
| `Domain/Order.cs` | Event-sourced Order aggregate. Business rules, state transitions, event replay |
| **Event Sourcing** | |
| `EventStore/EventStoreRepository.cs` | Persistence for event-sourced aggregates. Save/load events from EventStoreDB |
| **Read Model** | |
| `ReadModel/OrderReadModel.cs` | Denormalized order view + OrderReadDbContext for fast queries |
| `ReadModel/SagaStateDbContext.cs` | DbContext for MassTransit saga state persistence |
| **Saga** | |
| `Sagas/OrderStateMachine.cs` | Saga state machine. Defines states, events, transitions, and compensating actions |
| **gRPC** | |
| `GrpcServices/OrderGrpcServiceImpl.cs` | gRPC server implementation |
| `Protos/order.proto` | Protocol Buffer definition |
| **REST** | |
| `Controllers/OrdersController.cs` | REST API. Routes commands/queries through MediatR |

## Database Schema

### Order Read Model (order_read_db)

```sql
CREATE TABLE orders (
    OrderId          UUID PRIMARY KEY,
    CustomerId       UUID NOT NULL,
    Status           VARCHAR(50),
    TotalAmount      DECIMAL(18,2),
    ItemsJson        JSONB DEFAULT '[]',
    CreatedAt        TIMESTAMP,
    CompletedAt      TIMESTAMP,
    CancellationReason VARCHAR(500),
    LastEventSequence BIGINT
);

CREATE INDEX idx_orders_customer_id ON orders(CustomerId);
CREATE INDEX idx_orders_status ON orders(Status);
CREATE INDEX idx_orders_created_at ON orders(CreatedAt);
```

### Saga State (saga_state_db)

```sql
CREATE TABLE order_saga_state (
    CorrelationId  UUID PRIMARY KEY,
    CurrentState   VARCHAR(50),
    OrderId        UUID,
    CustomerId     UUID,
    StartedAt      TIMESTAMP,
    CompletedAt    TIMESTAMP,
    FailureReason  VARCHAR(500)
);
```

## Configuration and Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `ConnectionStrings__OrderReadDb` | `Host=localhost;Database=order_read_db;...` | PostgreSQL for read model |
| `ConnectionStrings__SagaStateDb` | `Host=localhost;Database=saga_state_db;...` | PostgreSQL for saga state |
| `EventStore__ConnectionString` | `esdb://localhost:2113?tls=false` | EventStoreDB connection |
| `RabbitMQ__Host` | `localhost` | RabbitMQ host |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `http://localhost:4317` | OpenTelemetry endpoint |

## How to Test

### Start the Service

```bash
dotnet run --project src/OrderService
```

Runs on `http://localhost:5020` (REST) and gRPC port.

### Place an Order (Triggers Full Saga)

```bash
curl -X POST http://localhost:5020/api/orders \
  -H "Content-Type: application/json" \
  -d '{
    "customerId": "550e8400-e29b-41d4-a716-446655440000",
    "items": [
      {"productId": "some-product-id", "quantity": 2}
    ]
  }'
```

**What happens:**
1. Order is created in EventStoreDB (OrderPlaced event)
2. Read model is updated in PostgreSQL
3. OrderPlaced event published to RabbitMQ
4. Saga picks it up, sends ReserveInventory command
5. InventoryService processes, publishes InventoryReserved
6. Saga sends ProcessPayment command
7. PaymentService processes (90% success rate), publishes result
8. Saga transitions to Completed or Failed

### Get Order Details

```bash
curl http://localhost:5020/api/orders/{order-id}
```

### Get Customer Orders

```bash
curl http://localhost:5020/api/orders/customer/{customer-id}
```

### Verify Event Store

Browse EventStoreDB at `http://localhost:2113` to see event streams.

### Verify Saga State

Query the saga_state_db PostgreSQL database to see saga state progression.

## Communication Patterns Demonstrated

| Pattern | Implementation |
|---------|---------------|
| **CQRS** | MediatR commands (write) vs queries (read), separate models |
| **Event Sourcing** | EventStoreDB, Order aggregate with replay, immutable event log |
| **Saga Orchestration** | MassTransit state machine coordinating distributed transaction |
| **Compensation** | Release inventory on payment failure, cancel order on any failure |
| **gRPC** | Binary protocol for internal service-to-service calls |
| **Database Per Service** | order_read_db and saga_state_db separate from other services |
| **OpenTelemetry** | Distributed tracing across the saga workflow |

## Dependencies

- **MediatR** - CQRS command/query dispatching
- **MassTransit** - Saga state machines and message bus
- **EventStore.Client** - EventStoreDB gRPC client
- **Entity Framework Core** - PostgreSQL ORM for read model and saga state
- **Npgsql** - PostgreSQL driver
- **Grpc.AspNetCore** - gRPC server framework
- **Serilog** - Structured logging
- **OpenTelemetry** - Distributed tracing
