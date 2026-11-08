using Davids.Services.Baskets.Core.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

namespace Davids.Services.Baskets.Api.Exceptions;

public sealed class BasketApiExceptionHandler(ILogger<BasketApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            if (!httpContext.Response.HasStarted)
            {
                httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            }

            return true;
        }

        var (status, code, reason) = exception switch
        {
            BasketProductUnavailableException domain => (StatusCodes.Status404NotFound, domain.Code, domain.Message),
            InsufficientBasketStockException domain => (StatusCodes.Status409Conflict, domain.Code, domain.Message),
            DomainException domain => (StatusCodes.Status400BadRequest, domain.Code, domain.Message),
            ArgumentException => (StatusCodes.Status400BadRequest, "invalid_request", "Invalid request."),
            InvalidDataException => (StatusCodes.Status400BadRequest, "invalid_request", "Malformed request payload."),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "invalid_request", "Invalid request body or parameters."),
            _ => (StatusCodes.Status500InternalServerError, "error", "There was an error processing the request.")
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Baskets API request failed.");
        }
        else if (exception is DomainException)
        {
            logger.LogWarning("Baskets domain error {Code}: {Message}", code, reason);
        }

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(new { code, reason }, cancellationToken);
        return true;
    }
}
