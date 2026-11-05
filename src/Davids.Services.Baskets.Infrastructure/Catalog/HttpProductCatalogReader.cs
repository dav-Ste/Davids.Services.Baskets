using System.Net;
using System.Net.Http.Json;
using Davids.Services.Baskets.Application.DTO;
using Davids.Services.Baskets.Application.Services;

namespace Davids.Services.Baskets.Infrastructure.Catalog;

public sealed class HttpProductCatalogReader(HttpClient httpClient) : IProductCatalogReader
{
    public async Task<ProductCatalogSnapshot?> GetActiveProductAsync(Guid productId, CancellationToken ct = default)
    {
        using var response = await httpClient.GetAsync($"products/{productId}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ProductCatalogSnapshot>(cancellationToken: ct);
    }
}
