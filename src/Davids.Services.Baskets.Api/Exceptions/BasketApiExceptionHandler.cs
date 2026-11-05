using Davids.Services.Baskets.Core.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

namespace Davids.Services.Baskets.Api.Exceptions;

public sealed class BasketApiExceptionHandler(ILogger<BasketApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DomainException domain)
        {
            return false;
        }

        var status = domain switch
        {
            BasketProductUnavailableException => StatusCodes.Status404NotFound,
            InsufficientBasketStockException => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };

        logger.LogWarning("Baskets domain error {Code}: {Message}", domain.Code, domain.Message);
        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(new
        {
            code = domain.Code,
            reason = domain.Message
        }, cancellationToken);
        return true;
    }
}
