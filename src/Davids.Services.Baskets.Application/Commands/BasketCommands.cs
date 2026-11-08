using System.Text.Json.Serialization;

namespace Davids.Services.Baskets.Application.Commands;

public sealed record UpsertBasketItem(
    [property: JsonRequired] Guid ProductId,
    [property: JsonRequired] int Quantity);
