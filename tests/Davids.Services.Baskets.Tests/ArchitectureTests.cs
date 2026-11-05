using Davids.Services.Baskets.Application.Services;
using Davids.Services.Baskets.Core.Entities;
using Davids.Services.Baskets.Infrastructure;
using Davids.Services.Baskets.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Davids.Services.Baskets.Tests;

[Trait("Category", "Unit")]
public sealed class ArchitectureTests
{
    [Fact]
    public void CoreLayerHasZeroOuterLayerOrInfrastructureFrameworkDependencies()
    {
        var coreRefs = typeof(CustomerBasket).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(coreRefs, n => n.StartsWith("Davids.", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(coreRefs, n => n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(coreRefs, n => n.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(coreRefs, n => n.StartsWith("RabbitMQ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApplicationLayerDependsOnlyOnCoreAndNeverOnInfrastructureOrApi()
    {
        var appRefs = typeof(BasketsService).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

        Assert.Contains("Davids.Services.Baskets.Core", appRefs, StringComparer.Ordinal);
        Assert.DoesNotContain("Davids.Services.Baskets.Infrastructure", appRefs, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Davids.Services.Baskets.Api", appRefs, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Davids.ServiceDefaults", appRefs, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(appRefs, n => n.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(appRefs, n => n.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InfrastructureAndApiLayersFollowInwardDependenciesAndNeverCoupleToSiblingServices()
    {
        var infraRefs = typeof(BasketsDbContext).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain("Davids.Services.Baskets.Api", infraRefs, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(infraRefs, n => n.StartsWith("Davids.Services.Products", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(infraRefs, n => n.StartsWith("Davids.Services.Customers", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(infraRefs, n => n.StartsWith("Davids.Services.Identity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EfCoreRuntimeModelMatchesMigrationsSnapshotWithoutPendingChanges()
    {
        using var designTimeDb = new DesignTimeFactory().CreateDbContext([]);
        Assert.False(designTimeDb.Database.HasPendingModelChanges());

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:basketsdb"] = "Server=localhost;Database=davids_baskets;Integrated Security=true;TrustServerCertificate=true",
                ["CustomerOidc:Authority"] = "https://localhost:7504",
                ["AdminOidc:Authority"] = "https://localhost:17325",
                ["Messaging:Enabled"] = "false"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddBasketsInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var runtimeDb = scope.ServiceProvider.GetRequiredService<BasketsDbContext>();
        Assert.False(runtimeDb.Database.HasPendingModelChanges());
    }
}
