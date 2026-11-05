using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Davids.Services.Baskets.Infrastructure.Persistence;

public sealed class BasketsDatabaseHealthCheck(BasketsDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Cannot connect to basketsdb.");
    }
}
