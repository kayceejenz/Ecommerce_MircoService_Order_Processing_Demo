namespace ApiGateway.Clients;

public class PaymentClient(HttpClient httpClient, ILogger<PaymentClient> logger)
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<PaymentClient> _logger = logger;

    public async Task<object?> GetPaymentStatusAsync(Guid orderId, CancellationToken ct = default)
    {
        _logger.LogInformation("Fetching payment status for order {OrderId}", orderId);
        var response = await _httpClient.GetAsync($"/api/payments/order/{orderId}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<object>(cancellationToken: ct);
    }

    public async Task<object> ProcessPaymentAsync(Guid orderId, decimal amount, CancellationToken ct = default)
    {
        _logger.LogInformation("Processing payment of {Amount} for order {OrderId}", amount, orderId);
        var payload = new { OrderId = orderId, Amount = amount };
        var response = await _httpClient.PostAsJsonAsync("/api/payments", payload, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<object>(cancellationToken: ct))!;
    }
}
