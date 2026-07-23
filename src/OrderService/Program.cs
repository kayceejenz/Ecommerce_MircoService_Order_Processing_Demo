using EventStore.Client;
using MassTransit;
using MediatR;
using OrderService.Cqrs.Commands;
using OrderService.Cqrs.Queries;
using OrderService.EventStore;
using OrderService.GrpcServices;
using OrderService.Protos;
using OrderService.ReadModel;
using OrderService.Sagas;
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

builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(PlaceOrderCommand).Assembly);
});

builder.Services.AddSingleton<EventStoreClient>(sp =>
{
    var connectionString = builder.Configuration.GetConnectionString("EventStore")
        ?? "esdb://localhost:2113?tls=false";

    var settings = EventStoreClientSettings.Create(connectionString);
    return new EventStoreClient(settings);
});

builder.Services.AddScoped<EventStoreRepository>();

builder.Services.AddDbContext<OrderReadDbContext>(options =>
{
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("OrderReadDb")
        ?? "Host=localhost;Database=order_read_db;Username=postgres;Password=postgres");
});

builder.Services.AddDbContext<SagaStateDbContext>(options =>
{
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("SagaStateDb")
        ?? "Host=localhost;Database=saga_state_db;Username=postgres;Password=postgres");
});

builder.Services.AddMassTransit(x =>
{
    // Register the saga state machine
    x.AddSagaStateMachine<OrderStateMachine, OrderSagaState>()
     .EntityFrameworkRepository(r =>
     {
         r.ExistingDbContext<SagaStateDbContext>();
         r.UsePostgres();  // Use PostgreSQL for saga state
     });

    // Register consumers (message handlers)
    // x.AddConsumer<SomeConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"] ?? "localhost", "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });

        // Configure saga endpoint
        cfg.ConfigureEndpoints(context);
    });
});

builder.Services.AddGrpc();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService("OrderService", serviceVersion: "1.0.0"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
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
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

#endregion

var app = builder.Build();

#region Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler("/error");
app.UseCors();

// Create databases and tables on startup
using (var scope = app.Services.CreateScope())
{
    var readDb = scope.ServiceProvider.GetRequiredService<OrderReadDbContext>();
    await readDb.Database.EnsureCreatedAsync();

    var sagaDb = scope.ServiceProvider.GetRequiredService<SagaStateDbContext>();
    await sagaDb.Database.EnsureCreatedAsync();
}

app.MapControllers();
app.MapGrpcService<OrderGrpcServiceImpl>();

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "OrderService",
    timestamp = DateTime.UtcNow
}));

#endregion

Log.Information("OrderService starting...");
app.Run();
