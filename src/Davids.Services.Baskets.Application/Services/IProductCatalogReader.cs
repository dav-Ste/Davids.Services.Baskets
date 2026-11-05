using Davids.Services.Baskets.Application.DTO;

namespace Davids.Services.Baskets.Application.Services;

public interface IProductCatalogReader
{
    Task<ProductCatalogSnapshot?> GetActiveProductAsync(Guid productId, CancellationToken ct = default);
}
