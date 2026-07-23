# NotificationService

Pure event consumer that logs notification messages for various order and inventory events. Acts as the end of event chains for notification paths.

## Purpose

NotificationService demonstrates the **consumer-only** pattern in event-driven architecture. It subscribes to events from other services and reacts by logging notification messages. In production, these log messages would be replaced with actual email, SMS, or push notification sends.

## Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                   NotificationService                         │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐  │
│  │                  MassTransit Consumers                  │  │
│  │                                                        │  │
│  │  OrderPlacedConsumer ────────────► Logs: "Order        │  │
│  │  (IConsumer<OrderPlaced>)         confirmation sent"   │  │
│  │                                                        │  │
│  │  PaymentSucceededConsumer ───────► Logs: "Payment      │  │
│  │  (IConsumer<PaymentSucceeded>)    confirmation sent"   │  │
│  │                                                        │  │
│  │  PaymentFailedConsumer ──────────► Logs: "Payment      │  │
│  │  (IConsumer<PaymentFailed>)       failure notified"    │  │
│  │                                                        │  │
│  │  InventoryLowConsumer ───────────► Logs: "Low stock    │  │
│  │  (IConsumer<InventoryLow>)        alert"               │  │
│  └────────────────────────────────────────────────────────┘  │
│                                                              │
│  No database. Stateless. Pure event consumer.                │
└──────────────────────────────────────────────────────────────┘
         ▲
         │
      RabbitMQ
  (4 event types)
```

## How It Works

NotificationService is the simplest service in the architecture. It has:

- **No database** - completely stateless
- **No outbound publishing** - it's a terminal consumer
- **No REST API** - only subscribes to events
- **4 consumers** - one for each notification type

### Consumer: OrderPlacedConsumer

Handles `OrderPlaced` events. Logs order confirmation:

```
Order confirmation sent to customer {CustomerId}
(Order: {OrderId}, Items: {ItemCount}, Total: {TotalAmount})
```

**In production:** Would send order confirmation email with order details, line items, and expected delivery date.

### Consumer: PaymentSucceededConsumer

Handles `PaymentSucceeded` events. Logs payment confirmation:

```
Payment confirmation sent to customer {CustomerId}
(Order: {OrderId}, Amount: {Amount}, Transaction: {TransactionId})
```

**In production:** Would send receipt email with payment details and transaction ID.

### Consumer: PaymentFailedConsumer

Handles `PaymentFailed` events. Logs payment failure notification:

```
Payment failure notification sent to customer {CustomerId}
(Order: {OrderId}, Amount: {Amount}, Reason: {Reason})
```

**In production:** Would send email/SMS alert about failed payment with retry instructions.

### Consumer: InventoryLowConsumer

Handles `InventoryLow` events. Logs low stock alert:

```
Low stock alert: {ProductName} has only {CurrentQuantity} units
(Threshold: {ReorderThreshold}, Detected: {DetectedAt})
```

**In production:** Would send alert to operations team, trigger restocking workflow, or update dashboard.

## Key Files

| File | Purpose |
|------|---------|
| `Program.cs` | Entry point. Configures 4 MassTransit consumers, OpenTelemetry, Swagger |
| `Consumers/OrderPlacedConsumer.cs` | Logs order confirmation notification |
| `Consumers/PaymentSucceededConsumer.cs` | Logs payment success notification |
| `Consumers/PaymentFailedConsumer.cs` | Logs payment failure notification |
| `Consumers/InventoryLowConsumer.cs` | Logs low stock alert |
| `appsettings.json` | RabbitMQ config, OpenTelemetry endpoint |

## Event Flow - End of Chain

NotificationService sits at the end of event chains:

### Order Confirmation Path
```
OrderService ──OrderPlaced──► InventoryService
                    │
                    ▼
             NotificationService
             (logs: "Order confirmed")
```

### Payment Success Path
```
PaymentService ──PaymentSucceeded──► OrderService (saga complete)
                          │
                          ▼
                   NotificationService
                   (logs: "Payment confirmed")
```

### Payment Failure Path
```
PaymentService ──PaymentFailed──► OrderService (compensate)
                       │
                       ▼
                NotificationService
                (logs: "Payment failed")
```

### Low Stock Alert Path
```
InventoryService ──InventoryLow──► NotificationService
                        │
                        ▼
                 (logs: "Low stock alert")
```

## Configuration and Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `RabbitMQ__Host` | `localhost` | RabbitMQ host |
| `RabbitMQ__Port` | `5672` | RabbitMQ port |
| `RabbitMQ__Username` | `guest` | RabbitMQ username |
| `RabbitMQ__Password` | `guest` | RabbitMQ password |
| `OpenTelemetry:OtlpEndpoint` | `http://localhost:4317` | OpenTelemetry OTLP endpoint |

**Docker Compose overrides:**
```yaml
environment:
  - RabbitMQ__Host=rabbitmq
```

## How to Test

### Start the Service

```bash
dotnet run --project src/NotificationService
```

### Verify Consumers Are Registered

Check logs on startup for MassTransit consumer registration:
- `PaymentSucceededConsumer`
- `PaymentFailedConsumer`
- `InventoryLowConsumer`
- `OrderPlacedConsumer`

### Test via Full System Flow

Place an order through the system and watch NotificationService logs:

```bash
# Place an order
curl -X POST http://localhost:5000/api/gateway/orders \
  -H "Content-Type: application/json" \
  -d '{"customerId": "550e8400-e29b-41d4-a716-446655440000", "items": [{"productId": "...", "quantity": 1}]}'
```

**Expected NotificationService logs (in order):**
1. `OrderPlacedConsumer`: "Order confirmation sent to customer..."
2. `PaymentSucceededConsumer`: "Payment confirmation sent to customer..." (90% of the time)
   - OR `PaymentFailedConsumer`: "Payment failure notification sent to customer..." (10% of the time)

### Test via RabbitMQ

1. Open RabbitMQ management UI at `http://localhost:15672`
2. Publish events directly to test individual consumers:

```json
// Publish OrderPlaced to notification-service queue
{
  "orderId": "550e8400-e29b-41d4-a716-446655440000",
  "customerId": "550e8400-e29b-41d4-a716-446655440001",
  "items": [{"productId": "...", "quantity": 2}],
  "totalAmount": 59.98,
  "placedAt": "2024-01-01T00:00:00Z"
}
```

3. Check NotificationService logs for the corresponding notification message

### Test Low Stock Alert

Trigger a low stock condition by ordering enough of a product to drop below the reorder threshold, then watch for the `InventoryLowConsumer` log message.

## Production Enhancements

In a production system, each consumer would:

| Consumer | Production Implementation |
|----------|--------------------------|
| `OrderPlacedConsumer` | Send confirmation email with order details, line items, tracking info |
| `PaymentSucceededConsumer` | Send receipt email with payment details, transaction ID |
| `PaymentFailedConsumer` | Send email/SMS with failure reason, retry link, support contact |
| `InventoryLowConsumer` | Send Slack/Teams alert to operations, trigger automated restocking |

Additional capabilities:
- Email service integration (SendGrid, AWS SES)
- SMS provider integration (Twilio, AWS SNS)
- Push notifications (Firebase, APNs)
- Dashboard real-time updates (SignalR WebSocket)
- Notification preferences per customer
- Rate limiting and deduplication

## Communication Patterns Demonstrated

| Pattern | Implementation |
|---------|---------------|
| **Pure Event Consumer** | Subscribes to events, does not publish |
| **Terminal Consumer** | End of event chain, no downstream effects |
| **Stateless Design** | No database, no state, horizontally scalable |
| **Fan-In** | Multiple event types converge into one service |
| **Loose Coupling** | Only depends on Shared.Contracts, not other services |

## Dependencies

- **MassTransit** - Message bus consumer framework
- **Serilog** - Structured logging
- **OpenTelemetry** - Distributed tracing (includes `MassTransit` activity source)
