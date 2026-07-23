using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using OpenTelemetry.Metrics;
using Polly;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ApiGateway.Clients;
using ApiGateway.Middleware;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

#region Configuration
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy());

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("ApiGateway"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter();
    });

var config = builder.Configuration;

static void ConfigureResilience(HttpStandardResilienceOptions options)
{
    options.Retry.MaxRetryAttempts = 3;
    options.Retry.Delay = TimeSpan.FromMilliseconds(200);
    options.Retry.BackoffType = DelayBackoffType.Exponential;
    options.Retry.UseJitter = true;

    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    options.CircuitBreaker.FailureRatio = 0.5;
    options.CircuitBreaker.MinimumThroughput = 10;
    options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);

    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
}

builder.Services.AddHttpClient<CatalogClient>(name: "CatalogClient", client =>
{
    client.BaseAddress = new Uri(config["CatalogService__BaseUrl"] ?? "http://localhost:5010");
})
.AddStandardResilienceHandler(ConfigureResilience);

builder.Services.AddHttpClient<OrderClient>(name: "OrderClient", client =>
{
    client.BaseAddress = new Uri(config["OrderService__BaseUrl"] ?? "http://localhost:5020");
})
.AddStandardResilienceHandler(ConfigureResilience);

builder.Services.AddHttpClient<PaymentClient>(name: "PaymentClient", client =>
{
    client.BaseAddress = new Uri(config["PaymentService__BaseUrl"] ?? "http://localhost:5040");
})
.AddStandardResilienceHandler(ConfigureResilience);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
#endregion

var app = builder.Build();

#region Middleware
app.UseMiddleware<CorrelationIdMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.ToString()
            })
        });
        await context.Response.WriteAsync(result);
    }
});

app.MapControllers();
#endregion

app.Run();
