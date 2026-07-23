# PaymentService

Saga participant that processes payments. Simulates payment processing with configurable success rates and publishes payment result events.

## Purpose

PaymentService handles the payment processing step of the order saga. It receives `ProcessPaymentCommand` from the OrderService saga, simulates payment processing, and publishes `PaymentSucceeded` or `PaymentFailed` events that the saga uses to determine the next step.

## Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                     PaymentService                            │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐  │
│  │              ProcessPaymentConsumer                    │  │
│  │           (IConsumer<ProcessPaymentCommand>)           │  │
│  │                                                        │  │
│  │  1. Receives command from RabbitMQ                     │  │
│  │  2. Simulates 500ms payment processing                 │  │
│  │  3. 90% success rate (random)                          │  │
│  │  4. Publishes PaymentSucceeded or PaymentFailed        │  │
│  └────────────────────────────────────────────────────────┘  │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐  │
│  │              PaymentsController                        │  │
│  │             (REST API - status queries)                │  │
│  │                                                        │  │
│  │  - In-memory payment records                          │  │
│  │  - GET /api/payments/{id}                             │  │
│  │  - GET /api/payments/order/{orderId}                  │  │
│  └────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────┘
         ▲                          │
         │                          ▼
      RabbitMQ               NotificationService
   (ProcessPaymentCommand)   (PaymentSucceeded,
                               PaymentFailed)
```

## How It Works

### ProcessPaymentConsumer

The core consumer (`Consumers/ProcessPaymentConsumer.cs:7-65`):

1. **Receives** `ProcessPaymentCommand` from RabbitMQ (sent by OrderService saga)
2. **Simulates** payment processing with a 500ms delay (`await Task.Delay(500)`)
3. **Determines** success randomly: 90% success, 10% failure
4. **On success:**
   - Generates a transaction ID (`TXN-{guid}`)
   - Publishes `PaymentSucceeded` event with order details and transaction ID
5. **On failure:**
   - Sets reason: "Payment declined by provider"
   - Publishes `PaymentFailed` event with failure details

### ProcessPaymentCommand (Local Copy)

PaymentService maintains its own copy of `ProcessPaymentCommand` (`Commands/ProcessPaymentCommand.cs`) rather than referencing Shared.Contracts. This avoids a cross-service dependency for a simple command type:

```csharp
public record ProcessPaymentCommand
{
    public Guid OrderId { get; init; }
    public Guid CustomerId { get; init; }
    public decimal Amount { get; init; }
}
```

### REST API

The `PaymentsController` provides payment status queries using in-memory storage:

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/payments/{id}` | Get payment by ID |
| GET | `/api/payments/order/{orderId}` | Get payments for an order |
| POST | `/api/payments` | Record a payment (for testing) |

**Note:** Payment records are stored in memory (`List<PaymentRecord>` with thread-safe locking). In production, these would persist to a database.

### Stateless Design

PaymentService has **no database**. This is intentional:

- Payment state is derived from events (PaymentSucceeded/PaymentFailed)
- The saga tracks payment status in its own state machine
- In-memory records are for debugging/testing only
- The service can be scaled horizontally with no shared state concerns

## Key Files

| File | Purpose |
|------|---------|
| `Program.cs` | Entry point. Configures MassTransit consumer, OpenTelemetry, Swagger, CORS |
| `Consumers/ProcessPaymentConsumer.cs` | Core consumer. Simulates payment, publishes success/failure events |
| `Commands/ProcessPaymentCommand.cs` | Local command definition (avoids cross-service dependency) |
| `Controllers/PaymentsController.cs` | REST API for payment status queries (in-memory) |
| `appsettings.json` | RabbitMQ config, OpenTelemetry endpoint |

## Event Publishing

### PaymentSucceeded

Published on successful payment:

```json
{
  "orderId": "550e8400-e29b-41d4-a716-446655440000",
  "customerId": "550e8400-e29b-41d4-a716-446655440001",
  "amount": 59.98,
  "transactionId": "TXN-a1b2c3d4e5f6g7h8i9j0",
  "processedAt": "2024-01-01T00:00:00Z"
}
```

**Consumed by:**
- OrderService saga (transition to Completed state)
- NotificationService (send payment confirmation)

### PaymentFailed

Published on payment failure:

```json
{
  "orderId": "550e8400-e29b-41d4-a716-446655440000",
  "customerId": "550e8400-e29b-41d4-a716-446655440001",
  "amount": 59.98,
  "reason": "Payment declined by provider",
  "failedAt": "2024-01-01T00:00:00Z"
}
```

**Consumed by:**
- OrderService saga (compensate: release inventory, cancel order)
- NotificationService (send payment failure notification)

## Configuration and Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `RabbitMQ__Host` | `localhost` | RabbitMQ host |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `http://localhost:4317` | OpenTelemetry endpoint |

**Docker Compose overrides:**
```yaml
environment:
  - RabbitMQ__Host=rabbitmq
```

## How to Test

### Start the Service

```bash
dotnet run --project src/PaymentService
```

Runs on `http://localhost:5040`.

### Health Check

```bash
curl http://localhost:5040/health
```

### Query Payment Status

```bash
# Get payments for an order
curl http://localhost:5040/api/payments/order/{order-id}

# Get specific payment
curl http://localhost:5040/api/payments/{payment-id}
```

### Test Event Consumption

1. Start RabbitMQ management UI at `http://localhost:15672`
2. Publish a `ProcessPaymentCommand` to the PaymentService queue
3. Watch logs for processing:
   - "Processing payment for Order {OrderId}"
   - "Payment succeeded for Order {OrderId}" (90% of the time)
   - "Payment failed for Order {OrderId}" (10% of the time)
4. Verify `PaymentSucceeded` or `PaymentFailed` event appears in RabbitMQ

### Test via Full Saga Flow

Place an order through ApiGateway and watch the PaymentService logs:
```bash
curl -X POST http://localhost:5000/api/gateway/orders \
  -H "Content-Type: application/json" \
  -d '{"customerId": "...", "items": [{"productId": "...", "quantity": 1}]}'
```

## Communication Patterns Demonstrated

| Pattern | Implementation |
|---------|---------------|
| **Saga Participant** | Receives ProcessPaymentCommand, publishes result events |
| **Event Publishing** | Publishes PaymentSucceeded/PaymentFailed |
| **Stateless Design** | No database, in-memory records for testing |
| **Simulated Processing** | 500ms delay, 90% success rate for demo purposes |
| **Local Command Type** | ProcessPaymentCommand defined locally to avoid cross-service dependency |

## Dependencies

- **MassTransit** - Message bus consumer framework
- **Serilog** - Structured logging
- **OpenTelemetry** - Distributed tracing
