using Davids.Services.Baskets.Application.Services;
using Davids.Services.Baskets.Core.Repositories;
using Davids.Services.Baskets.Infrastructure.Auth;
using Davids.Services.Baskets.Infrastructure.Catalog;
using Davids.Services.Baskets.Infrastructure.Messaging;
using Davids.Services.Baskets.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Davids.Services.Baskets.Infrastructure;

public static class BasketsInfrastructureExtensions
{
    public const string CustomerScheme = "CustomerBearer";

    public static IServiceCollection AddBasketsInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var customerOidc = configuration.GetSection("CustomerOidc").Get<OidcAuthoritySettings>()
            ?? new OidcAuthoritySettings { Audience = "davids-services" };
        customerOidc.Audience ??= "davids-services";
        customerOidc.ValidateAndResolveStaticPublicKey("CustomerOidc");
        services.AddSingleton(customerOidc);

        var connectionString = configuration.GetConnectionString("basketsdb")
            ?? throw new InvalidOperationException("Missing basketsdb connection.");
        services.AddDbContext<BasketsDbContext>(options => options.UseSqlServer(connectionString));

        var productsBaseUrl = configuration["ProductsApi:BaseUrl"] ?? "http://localhost:5006/";
        services.AddHttpClient<IProductCatalogReader, HttpProductCatalogReader>(client =>
        {
            client.BaseAddress = new Uri(productsBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddScoped<ICustomerBasketRepository, CustomerBasketRepository>();
        services.AddScoped<IBasketsService, BasketsService>();

        if (configuration.GetValue("Messaging:Enabled", true))
        {
            services.AddHostedService<BasketsMessageWorker>();
        }

        services.AddHealthChecks().AddCheck<BasketsDatabaseHealthCheck>("baskets-database");
        return services;
    }
}
