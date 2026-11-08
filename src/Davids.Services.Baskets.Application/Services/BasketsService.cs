using Davids.Services.Baskets.Application.Commands;
using Davids.Services.Baskets.Application.DTO;
using Davids.Services.Baskets.Core.Entities;
using Davids.Services.Baskets.Core.Exceptions;
using Davids.Services.Baskets.Core.Repositories;

namespace Davids.Services.Baskets.Application.Services;

public sealed class BasketsService(
    ICustomerBasketRepository repository,
    IProductCatalogReader catalogReader) : IBasketsService
{
    public async Task<BasketDto> GetBasketAsync(Guid customerId, CancellationToken ct = default)
    {
        var basket = await repository.GetAsync(customerId, ct);
        return basket is null
            ? new BasketDto(customerId, [], 0, 0m, "GBP", DateTime.UtcNow)
            : Map(basket);
    }

    public async Task<BasketDto> UpsertItemAsync(
        Guid customerId,
        UpsertBasketItem command,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var basket = await repository.GetAsync(customerId, ct);

        if (command.Quantity == 0)
        {
            if (basket is null)
            {
                return new BasketDto(customerId, [], 0, 0m, "GBP", now);
            }

            basket.RemoveItem(command.ProductId, now);
            await repository.SaveChangesAsync(ct);
            return Map(basket);
        }

        var product = await catalogReader.GetActiveProductAsync(command.ProductId, ct);
        if (product is null || !product.IsActive)
        {
            throw new BasketProductUnavailableException(command.ProductId);
        }

        if (basket is null)
        {
            basket = new CustomerBasket(customerId, now);
            await repository.AddAsync(basket, ct);
        }

        basket.UpsertItem(
            product.Id,
            product.Sku,
            product.Name,
            product.Price,
            product.Currency,
            command.Quantity,
            product.StockQuantity,
            now);

        await repository.SaveChangesAsync(ct);
        return Map(basket);
    }

    public async Task<BasketDto> RemoveItemAsync(Guid customerId, Guid productId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var basket = await repository.GetAsync(customerId, ct);
        if (basket is null)
        {
            return new BasketDto(customerId, [], 0, 0m, "GBP", now);
        }

        basket.RemoveItem(productId, now);
        await repository.SaveChangesAsync(ct);
        return Map(basket);
    }

    public async Task ClearBasketAsync(Guid customerId, CancellationToken ct = default)
    {
        var basket = await repository.GetAsync(customerId, ct);
        if (basket is null)
        {
            return;
        }

        basket.Clear(DateTime.UtcNow);
        await repository.SaveChangesAsync(ct);
    }

    private static BasketDto Map(CustomerBasket basket)
    {
        var lines = basket.Items
            .OrderBy(x => x.ProductName)
            .Select(x => new BasketItemDto(
                x.ProductId,
                x.Sku,
                x.ProductName,
                x.UnitPrice,
                x.Currency,
                x.Quantity,
                decimal.Round(x.UnitPrice * x.Quantity, 2, MidpointRounding.AwayFromZero)))
            .ToArray();

        var totalQty = lines.Sum(x => x.Quantity);
        var totalAmount = lines.Sum(x => x.LineTotal);
        return new BasketDto(
            basket.CustomerId,
            lines,
            totalQty,
            totalAmount,
            basket.Currency,
            DateTime.SpecifyKind(basket.UpdatedAt, DateTimeKind.Utc));
    }
}
