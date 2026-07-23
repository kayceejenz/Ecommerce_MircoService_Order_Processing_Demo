using EventStore.Client;
using System.Text.Json;
using System.Text;

namespace OrderService.EventStore;

public class EventStoreRepository
{
    private readonly EventStoreClient _client;
    private readonly ILogger<EventStoreRepository> _logger;

    public EventStoreRepository(EventStoreClient client, ILogger<EventStoreRepository> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <summary>
    /// Save new events to EventStoreDB.
    /// Uses StreamState.Any for simplicity (no optimistic concurrency in demo).
    /// </summary>
    public async Task SaveAsync(Domain.Order order, CancellationToken ct = default)
    {
        var events = order.GetUncommittedEvents();
        if (events.Count == 0)
        {
            _logger.LogDebug("No events to save for order {OrderId}", order.Id);
            return;
        }

        var streamName = GetStreamName(order.Id);

        _logger.LogInformation(
            "Saving {EventCount} events to stream {StreamName}",
            events.Count,
            streamName);

        var eventData = events.Select(MapToEventData).ToArray();

        await _client.AppendToStreamAsync(
            streamName,
            StreamState.Any,
            eventData,
            cancellationToken: ct);

        order.ClearUncommittedEvents();

        _logger.LogInformation(
            "Successfully saved {EventCount} events for order {OrderId}",
            events.Count,
            order.Id);
    }

    /// <summary>
    /// Load an order by replaying all its events from EventStoreDB.
    /// </summary>
    public async Task<Domain.Order?> LoadAsync(Guid orderId, CancellationToken ct = default)
    {
        var streamName = GetStreamName(orderId);

        _logger.LogDebug("Loading order {OrderId} from stream {StreamName}", orderId, streamName);

        var events = new List<ResolvedEvent>();

        var result = _client.ReadStreamAsync(
            Direction.Forwards,
            streamName,
            StreamPosition.Start,
            cancellationToken: ct);

        await foreach (var @event in result.WithCancellation(ct))
        {
            events.Add(@event);
        }

        if (events.Count == 0)
        {
            _logger.LogDebug("No events found for order {OrderId}", orderId);
            return null;
        }

        var order = new Domain.Order();
        foreach (var @event in events)
        {
            var domainEvent = DeserializeEvent(@event);
            if (domainEvent is not null)
            {
                order.Replay(domainEvent);
            }
        }

        _logger.LogInformation(
            "Rebuilt order {OrderId} from {EventCount} events. Status: {Status}",
            orderId, events.Count, order.Status);

        return order;
    }

    private static EventData MapToEventData(object @event)
    {
        var typeName = @event.GetType().Name;
        var json = JsonSerializer.Serialize(@event, @event.GetType());

        return new EventData(
            Uuid.FromGuid(Guid.NewGuid()),
            typeName,
            Encoding.UTF8.GetBytes(json));
    }

    private static object? DeserializeEvent(ResolvedEvent resolvedEvent)
    {
        var typeName = resolvedEvent.Event.EventType;
        var json = Encoding.UTF8.GetString(resolvedEvent.Event.Data.Span);

        return typeName switch
        {
            "OrderPlaced" => JsonSerializer.Deserialize<Shared.Contracts.Events.OrderPlaced>(json),
            "OrderCancelled" => JsonSerializer.Deserialize<Shared.Contracts.Events.OrderCancelled>(json),
            _ => null
        };
    }

    private static string GetStreamName(Guid orderId) => $"order-{orderId}";
}
