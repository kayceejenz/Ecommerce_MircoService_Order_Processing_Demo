# ApiGateway

Single entry point for external clients. Routes requests to backend microservices with built-in resilience, correlation tracking, and observability.

## Purpose

The ApiGateway acts as the front door to the entire microservices system. External clients (browsers, mobile apps, third-party integrations) talk only to the Gateway, which fans out requests to the appropriate backend services. This provides:

- **Single URL**: Clients don't need to know internal service addresses
- **Resilience**: Automatic retries, circuit breakers, timeouts via Polly v8
- **Correlation Tracking**: Every request gets a unique ID that flows through all services
- **Security Boundary**: Future home for authentication, rate limiting, and API key validation

## Architecture

```
                    ┌─────────────────────────────┐
                    │       External Client        │
                    │   (Browser / Mobile App)     │
                    └──────────────┬──────────────┘
                                   │ HTTP
                                   ▼
                    ┌─────────────────────────────┐
                    │      CorrelationIdMiddleware  │
                    │   (X-Correlation-Id header)  │
                    └──────────────┬──────────────┘
                                   │
                                   ▼
                    ┌─────────────────────────────┐
                    │       GatewayController      │
                    │   (Routes to typed clients)  │
                    └──────┬──────┬──────┬────────┘
                           │      │      │
              ┌────────────┘      │      └────────────┐
              │                   │                   │
              ▼                   ▼                   ▼
    ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐
    │  CatalogClient  │ │   OrderClient   │ │  PaymentClient  │
    │  (Polly v8)     │ │  (Polly v8)     │ │  (Polly v8)     │
    └────────┬────────┘ └────────┬────────┘ └────────┬────────┘
             │                   │                   │
             ▼                   ▼                   ▼
    ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐
    │ CatalogService  │ │  OrderService   │ │ PaymentService  │
    │   :5010         │ │   :5020         │ │   :5040         │
    └─────────────────┘ └─────────────────┘ └─────────────────┘
```

## How It Works

### Request Flow

1. **Client sends request** to ApiGateway (e.g., `POST /api/gateway/orders`)
2. **CorrelationIdMiddleware** extracts or generates an `X-Correlation-Id` header and pushes it into Serilog's `LogContext`
3. **GatewayController** routes the request to the appropriate typed HTTP client
4. **Typed Client** (CatalogClient, OrderClient, or PaymentClient) forwards to the backend service
5. **Polly v8 resilience pipeline** handles retries, circuit breaking, and timeouts automatically
6. **Response** flows back through the same pipeline to the client

### Correlation ID Middleware

Defined in `Middleware/CorrelationIdMiddleware.cs:1-29`:

- Extracts `X-Correlation-Id` from incoming request headers
- If missing, generates a new GUID and sets it
- Stores it in `HttpContext.Items["CorrelationId"]`
- Echoes it back in the response header
- Pushes it into Serilog's `LogContext` so all log entries during this request include the correlation ID

This means you can grep logs by correlation ID to trace a single request across all services.

### Polly v8 Resilience Pipeline

Configured in `Program.cs:49-62`, applied to all three typed clients:

```csharp
static void ConfigureResilience(HttpStandardResilienceOptions options)
{
    // RETRY POLICY
    options.Retry.MaxRetryAttempts = 3;        // Up to 3 retries
    options.Retry.Delay = TimeSpan.FromMilliseconds(200); // 200ms base delay
    options.Retry.BackoffType = DelayBackoffType.Exponential; // 200ms, 400ms, 800ms
    options.Retry.UseJitter = true;            // Add randomness to prevent thundering herd

    // CIRCUIT BREAKER
    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30); // Monitor over 30s window
    options.CircuitBreaker.FailureRatio = 0.5;  // Open circuit at 50% failure rate
    options.CircuitBreaker.MinimumThroughput = 10; // Need at least 10 requests to evaluate
    options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30); // Stay open for 30s

    // TIMEOUT
    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10); // 10s per attempt
}
```

**How it works:**

1. **Normal operation**: Requests flow through normally
2. **Intermittent failures**: Retries with exponential backoff (200ms, 400ms, 800ms) plus jitter
3. **Sustained failures**: Circuit opens after 50% failures in a 30s window with at least 10 requests. Fast-fails for 30 seconds
4. **Recovery**: After 30s break duration, circuit goes half-open and allows a test request
5. **Slow responses**: Each attempt times out after 10 seconds

### Typed HTTP Clients

Each client is a strongly-typed wrapper around `HttpClient` registered via `AddHttpClient<T>()`.

#### CatalogClient (`Clients/CatalogClient.cs`)

Routes to CatalogService (`http://localhost:5010`)

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GetProductsAsync` | `GET /api/products` | List all products |
| `GetProductByIdAsync` | `GET /api/products/{id}` | Get single product |
| `GetInventoryAsync` | `GET /api/inventory` | List all inventory |
| `GetInventoryByProductAsync` | `GET /api/inventory/{productId}` | Get inventory for one product |

#### OrderClient (`Clients/OrderClient.cs`)

Routes to OrderService (`http://localhost:5020`)

| Method | Endpoint | Description |
|--------|----------|-------------|
| `CreateOrderAsync` | `POST /api/orders` | Place a new order |
| `GetOrderByIdAsync` | `GET /api/orders/{id}` | Get order details |
| `GetOrdersByCustomerAsync` | `GET /api/orders/customer/{customerId}` | Get customer's orders |

#### PaymentClient (`Clients/PaymentClient.cs`)

Routes to PaymentService (`http://localhost:5040`)

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GetPaymentStatusAsync` | `GET /api/payments/order/{orderId}` | Get payment status for order |
| `ProcessPaymentAsync` | `POST /api/payments` | Process a payment |

### GatewayController

Defined in `Controllers/GatewayController.cs:1-111`. Exposes a unified REST API that maps to backend service endpoints:

| Method | Gateway Endpoint | Backend Client | Backend Endpoint |
|--------|-----------------|----------------|-----------------|
| GET | `/api/gateway/products` | CatalogClient | `GET /api/products` |
| GET | `/api/gateway/products/{id}` | CatalogClient | `GET /api/products/{id}` |
| GET | `/api/gateway/inventory` | CatalogClient | `GET /api/inventory` |
| GET | `/api/gateway/inventory/{productId}` | CatalogClient | `GET /api/inventory/{productId}` |
| POST | `/api/gateway/orders` | OrderClient | `POST /api/orders` |
| GET | `/api/gateway/orders/{id}` | OrderClient | `GET /api/orders/{id}` |
| GET | `/api/gateway/orders/customer/{customerId}` | OrderClient | `GET /api/orders/customer/{customerId}` |
| GET | `/api/gateway/payments/order/{orderId}` | PaymentClient | `GET /api/payments/order/{orderId}` |
| POST | `/api/gateway/payments` | PaymentClient | `POST /api/payments` |

## Key Files

| File | Purpose |
|------|---------|
| `Program.cs` | Application entry point. Configures Serilog, health checks, OpenTelemetry, Polly resilience, typed clients, Swagger, middleware pipeline |
| `Controllers/GatewayController.cs` | REST API routing to backend services via typed clients |
| `Middleware/CorrelationIdMiddleware.cs` | Extracts/generates correlation ID, pushes to Serilog LogContext |
| `Clients/CatalogClient.cs` | Typed HTTP client for CatalogService |
| `Clients/OrderClient.cs` | Typed HTTP client for OrderService |
| `Clients/PaymentClient.cs` | Typed HTTP client for PaymentService |
| `appsettings.json` | Default service URLs and Serilog configuration |
| `Dockerfile` | Container build configuration |

## Configuration and Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `CatalogService__BaseUrl` | `http://localhost:5010` | CatalogService address |
| `OrderService__BaseUrl` | `http://localhost:5020` | OrderService address |
| `PaymentService__BaseUrl` | `http://localhost:5040` | PaymentService address |

**Docker Compose overrides:**
```yaml
environment:
  - CatalogService__BaseUrl=http://catalog-service:5010
  - OrderService__BaseUrl=http://order-service:5020
  - PaymentService__BaseUrl=http://payment-service:5040
```

**Serilog configuration** (in `appsettings.json`):
- Minimum level: Information
- Overrides: Microsoft and System set to Warning
- Enrichers: FromLogContext, WithMachineName, WithThreadId
- Output: Console

## How to Test

### Start the Gateway

```bash
dotnet run --project src/ApiGateway
```

Gateway runs on `http://localhost:5000` (or `http://localhost:5001` for HTTPS).

### Health Check

```bash
curl http://localhost:5000/health
```

Returns JSON with gateway status and all dependency health checks.

### Swagger UI

Available in Development mode at `http://localhost:5000/swagger`.

### Test Endpoints

```bash
# Get products
curl http://localhost:5000/api/gateway/products

# Get single product
curl http://localhost:5000/api/gateway/products/{product-id}

# Create order
curl -X POST http://localhost:5000/api/gateway/orders \
  -H "Content-Type: application/json" \
  -d '{"customerId": "550e8400-e29b-41d4-a716-446655440000", "items": [{"productId": "...", "quantity": 2}]}'

# Get order status
curl http://localhost:5000/api/gateway/orders/{order-id}

# Test correlation ID
curl -H "X-Correlation-Id: my-test-id-123" http://localhost:5000/api/gateway/products
```

### Testing Resilience

To test retry/circuit breaker behavior:
1. Stop the CatalogService
2. Send requests to `/api/gateway/products`
3. Observe 3 retries with exponential backoff in Gateway logs
4. After enough failures, circuit opens and requests fail fast
5. Restart CatalogService, wait 30s, circuit closes

## Communication Patterns Demonstrated

| Pattern | Implementation |
|---------|---------------|
| **API Gateway** | Single entry point routing to multiple backend services |
| **Typed HTTP Clients** | Strongly-typed wrappers around HttpClient |
| **Resilience Pipeline** | Polly v8 retry, circuit breaker, timeout |
| **Correlation ID** | Cross-service request tracing via headers |
| **Structured Logging** | Serilog with contextual properties |
| **Health Checks** | `/health` endpoint with dependency reporting |
| **OpenTelemetry** | Distributed tracing with OTLP export |

## Dependencies

- **Polly v8** - Resilience and fault handling
- **Serilog** - Structured logging
- **OpenTelemetry** - Distributed tracing and metrics
- **Microsoft.Extensions.Http.Resilience** - Standard resilience handler
- **Swashbuckle** - Swagger/OpenAPI
