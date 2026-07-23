using Shared.Contracts.Dtos;

namespace ApiGateway.Clients;

public class CatalogClient(HttpClient httpClient, ILogger<CatalogClient> logger)
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<CatalogClient> _logger = logger;

    public async Task<List<ProductDto>> GetProductsAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Fetching products from CatalogService");
        var response = await _httpClient.GetAsync("/api/products", ct);
        response.EnsureSuccessStatusCode();
        var products = await response.Content.ReadFromJsonAsync<List<ProductDto>>(cancellationToken: ct);
        return products ?? [];
    }

    public async Task<ProductDto?> GetProductByIdAsync(Guid id, CancellationToken ct = default)
    {
        _logger.LogInformation("Fetching product {ProductId} from CatalogService", id);
        var response = await _httpClient.GetAsync($"/api/products/{id}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ProductDto>(cancellationToken: ct);
    }

    public async Task<List<InventoryDto>> GetInventoryAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Fetching inventory from CatalogService");
        var response = await _httpClient.GetAsync("/api/inventory", ct);
        response.EnsureSuccessStatusCode();
        var inventory = await response.Content.ReadFromJsonAsync<List<InventoryDto>>(cancellationToken: ct);
        return inventory ?? [];
    }

    public async Task<InventoryDto?> GetInventoryByProductAsync(Guid productId, CancellationToken ct = default)
    {
        _logger.LogInformation("Fetching inventory for product {ProductId}", productId);
        var response = await _httpClient.GetAsync($"/api/inventory/{productId}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<InventoryDto>(cancellationToken: ct);
    }
}
