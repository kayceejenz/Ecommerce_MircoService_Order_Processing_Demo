using Shared.Contracts.Dtos;

namespace ApiGateway.Clients;

public class OrderClient(HttpClient httpClient, ILogger<OrderClient> logger)
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<OrderClient> _logger = logger;

    public async Task<OrderDto> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Creating order for customer {CustomerId}", request.CustomerId);
        var response = await _httpClient.PostAsJsonAsync("/api/orders", request, ct);
        response.EnsureSuccessStatusCode();
        var order = await response.Content.ReadFromJsonAsync<OrderDto>(cancellationToken: ct);
        return order!;
    }

    public async Task<OrderDto?> GetOrderByIdAsync(Guid orderId, CancellationToken ct = default)
    {
        _logger.LogInformation("Fetching order {OrderId}", orderId);
        var response = await _httpClient.GetAsync($"/api/orders/{orderId}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OrderDto>(cancellationToken: ct);
    }

    public async Task<List<OrderDto>> GetOrdersByCustomerAsync(Guid customerId, CancellationToken ct = default)
    {
        _logger.LogInformation("Fetching orders for customer {CustomerId}", customerId);
        var response = await _httpClient.GetAsync($"/api/orders/customer/{customerId}", ct);
        response.EnsureSuccessStatusCode();
        var orders = await response.Content.ReadFromJsonAsync<List<OrderDto>>(cancellationToken: ct);
        return orders ?? [];
    }
}
