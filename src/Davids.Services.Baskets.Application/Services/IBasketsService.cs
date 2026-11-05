using Davids.Services.Baskets.Application.Commands;
using Davids.Services.Baskets.Application.DTO;

namespace Davids.Services.Baskets.Application.Services;

public interface IBasketsService
{
    Task<BasketDto> GetBasketAsync(Guid customerId, CancellationToken ct = default);
    Task<BasketDto> UpsertItemAsync(Guid customerId, UpsertBasketItem command, CancellationToken ct = default);
    Task<BasketDto> RemoveItemAsync(Guid customerId, Guid productId, CancellationToken ct = default);
    Task ClearBasketAsync(Guid customerId, CancellationToken ct = default);
}
