namespace Davids.Services.Baskets.Core.Exceptions;

public abstract class DomainException(string message) : Exception(message)
{
    public abstract string Code { get; }
}

public sealed class InvalidBasketException(string message) : DomainException(message)
{
    public override string Code => "invalid_basket";
}

public sealed class BasketProductUnavailableException(Guid productId)
    : DomainException($"Product '{productId}' is not available for purchase.")
{
    public override string Code => "basket_product_unavailable";
}

public sealed class InsufficientBasketStockException(Guid productId, int requested, int available)
    : DomainException($"Requested quantity ({requested}) exceeds available stock ({available}) for product '{productId}'.")
{
    public override string Code => "insufficient_basket_stock";
}
