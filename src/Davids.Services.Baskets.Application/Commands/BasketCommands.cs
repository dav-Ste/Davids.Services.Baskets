namespace Davids.Services.Baskets.Application.Commands;

public sealed record UpsertBasketItem(Guid ProductId, int Quantity);
