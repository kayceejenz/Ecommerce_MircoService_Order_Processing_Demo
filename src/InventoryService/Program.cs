using InventoryService.Consumers;
using InventoryService.Data;
using InventoryService.Data.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

#region Configuration

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddDbContext<InventoryDbContext>(options =>
{
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("InventoryDb")
        ?? "Host=localhost;Database=inventory_db;Username=postgres;Password=postgres",
        npgsqlOptions =>
        {
            npgsqlOptions.MigrationsAssembly(typeof(InventoryDbContext).Assembly.FullName);
            npgsqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorCodesToAdd: null);
        });
});

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<OrderPlacedConsumer>();
    x.AddConsumer<OrderCancelledConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"] ?? "localhost", "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });

        cfg.ConfigureEndpoints(context);
    });
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService("InventoryService", serviceVersion: "1.0.0"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(
                builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
                ?? "http://localhost:4317");
        }));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

#endregion

var app = builder.Build();

#region Middleware
// Development-only middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Global exception handling
app.UseExceptionHandler("/error");

// CORS middleware
app.UseCors();

app.MapControllers();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "InventoryService",
    timestamp = DateTime.UtcNow
}));

// Create database and tables on startup, seed sample data
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    await db.Database.EnsureCreatedAsync();
    await SeedDataAsync(db);
}

#endregion

Log.Information("InventoryService starting...");
app.Run();

#region helper
static async Task SeedDataAsync(InventoryDbContext db)
{
    if (await db.InventoryItems.AnyAsync())
        return;

    var items = new[]
    {
        new InventoryItem
        {
            Id = Guid.NewGuid(),
            ProductName = "Wireless Mouse",
            AvailableQuantity = 150,
            ReservedQuantity = 0,
            ReorderThreshold = 10
        },
        new InventoryItem
        {
            Id = Guid.NewGuid(),
            ProductName = "Mechanical Keyboard",
            AvailableQuantity = 75,
            ReservedQuantity = 0,
            ReorderThreshold = 10
        },
        new InventoryItem
        {
            Id = Guid.NewGuid(),
            ProductName = "USB-C Hub",
            AvailableQuantity = 200,
            ReservedQuantity = 0,
            ReorderThreshold = 15
        },
        new InventoryItem
        {
            Id = Guid.NewGuid(),
            ProductName = "Cotton T-Shirt",
            AvailableQuantity = 500,
            ReservedQuantity = 0,
            ReorderThreshold = 25
        },
        new InventoryItem
        {
            Id = Guid.NewGuid(),
            ProductName = "Running Shoes",
            AvailableQuantity = 100,
            ReservedQuantity = 0,
            ReorderThreshold = 10
        }
    };

    db.InventoryItems.AddRange(items);
    await db.SaveChangesAsync();

    Log.Information("Seeded {Count} sample inventory items", items.Length);
}
#endregion