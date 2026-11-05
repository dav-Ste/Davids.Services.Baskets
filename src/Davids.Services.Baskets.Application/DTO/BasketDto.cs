namespace Davids.Services.Baskets.Application.DTO;

public sealed record BasketItemDto(
    Guid ProductId,
    string Sku,
    string ProductName,
    decimal UnitPrice,
    string Currency,
    int Quantity,
    decimal LineTotal);

public sealed record BasketDto(
    Guid CustomerId,
    IReadOnlyList<BasketItemDto> Items,
    int TotalItems,
    decimal TotalAmount,
    string Currency,
    DateTime UpdatedAt);

public sealed record ProductCatalogSnapshot(
    Guid Id,
    string Sku,
    string Name,
    decimal Price,
    string Currency,
    int StockQuantity,
    bool IsActive);
