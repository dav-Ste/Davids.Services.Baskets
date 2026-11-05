using Davids.Services.Baskets.Core.Exceptions;

namespace Davids.Services.Baskets.Core.Entities;

public sealed class CustomerBasket
{
    private readonly List<BasketItem> items = [];

    public Guid CustomerId { get; private set; }
    public string Currency { get; private set; } = "GBP";
    public DateTime UpdatedAt { get; private set; }
    public IReadOnlyCollection<BasketItem> Items => items;

    private CustomerBasket() { }

    public CustomerBasket(Guid customerId, DateTime nowUtc)
    {
        if (customerId == Guid.Empty)
        {
            throw new InvalidBasketException("Customer id cannot be empty.");
        }

        CustomerId = customerId;
        Currency = "GBP";
        UpdatedAt = nowUtc;
    }

    public void UpsertItem(
        Guid productId,
        string sku,
        string productName,
        decimal unitPrice,
        string currency,
        int quantity,
        int availableStock,
        DateTime nowUtc)
    {
        if (productId == Guid.Empty)
        {
            throw new InvalidBasketException("Product id cannot be empty.");
        }

        if (quantity < 0 || quantity > 99)
        {
            throw new InvalidBasketException("Item quantity must be between 0 and 99.");
        }

        var existing = items.FirstOrDefault(x => x.ProductId == productId);
        if (quantity == 0)
        {
            if (existing is not null)
            {
                items.Remove(existing);
                UpdatedAt = nowUtc;
            }
            return;
        }

        if (quantity > availableStock)
        {
            throw new InsufficientBasketStockException(productId, quantity, availableStock);
        }

        var normalizedCurrency = string.IsNullOrWhiteSpace(currency) ? "GBP" : currency.Trim().ToUpperInvariant();
        if (items.Count > 0 && !string.Equals(Currency, normalizedCurrency, StringComparison.Ordinal))
        {
            throw new InvalidBasketException($"Cannot mix currencies ('{Currency}' and '{normalizedCurrency}') in one basket.");
        }

        Currency = normalizedCurrency;
        if (existing is null)
        {
            items.Add(new BasketItem(CustomerId, productId, sku, productName, unitPrice, normalizedCurrency, quantity));
        }
        else
        {
            existing.UpdateSnapshot(sku, productName, unitPrice, normalizedCurrency, quantity);
        }

        UpdatedAt = nowUtc;
    }

    public bool RemoveItem(Guid productId, DateTime nowUtc)
    {
        var existing = items.FirstOrDefault(x => x.ProductId == productId);
        if (existing is null)
        {
            return false;
        }

        items.Remove(existing);
        UpdatedAt = nowUtc;
        return true;
    }

    public void Clear(DateTime nowUtc)
    {
        items.Clear();
        UpdatedAt = nowUtc;
    }
}

public sealed class BasketItem
{
    public Guid CustomerId { get; private set; }
    public Guid ProductId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string ProductName { get; private set; } = string.Empty;
    public decimal UnitPrice { get; private set; }
    public string Currency { get; private set; } = "GBP";
    public int Quantity { get; private set; }

    private BasketItem() { }

    internal BasketItem(
        Guid customerId,
        Guid productId,
        string sku,
        string productName,
        decimal unitPrice,
        string currency,
        int quantity)
    {
        CustomerId = customerId;
        ProductId = productId;
        UpdateSnapshot(sku, productName, unitPrice, currency, quantity);
    }

    internal void UpdateSnapshot(string sku, string productName, decimal unitPrice, string currency, int quantity)
    {
        if (unitPrice <= 0m)
        {
            throw new InvalidBasketException("Unit price must be positive.");
        }

        Sku = sku.Trim();
        ProductName = productName.Trim();
        UnitPrice = decimal.Round(unitPrice, 2, MidpointRounding.AwayFromZero);
        Currency = currency.Trim().ToUpperInvariant();
        Quantity = quantity;
    }
}
