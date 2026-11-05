using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Davids.Services.Baskets.Application.Commands;
using Davids.Services.Baskets.Application.DTO;
using Davids.Services.Baskets.Application.Services;
using Davids.Services.Baskets.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Davids.Services.Baskets.Tests;

public sealed class FakeCatalogReader : IProductCatalogReader
{
    public ConcurrentDictionary<Guid, ProductCatalogSnapshot> Products { get; } = new();

    public Task<ProductCatalogSnapshot?> GetActiveProductAsync(Guid productId, CancellationToken ct = default)
        => Task.FromResult(Products.TryGetValue(productId, out var p) && p.IsActive ? p : null);
}

public sealed class BasketsFactory : WebApplicationFactory<Program>
{
    private const string CustomerIssuer = "https://identity.davids.test/";
    private const string AdminIssuer = "https://admin-identity.davids.test/";

    private static readonly RSA CustomerRsa = RSA.Create(2048);
    private static readonly RSA AdminRsa = RSA.Create(2048);

    private static readonly string CustomerPublicKeyPem = CustomerRsa.ExportSubjectPublicKeyInfoPem();
    private static readonly string AdminPublicKeyPem = AdminRsa.ExportSubjectPublicKeyInfoPem();

    private static readonly RsaSecurityKey CustomerKey = CreateKey(CustomerRsa);
    private static readonly RsaSecurityKey AdminKey = CreateKey(AdminRsa);

    private readonly SqliteConnection connection = new("Data Source=:memory:");
    public FakeCatalogReader Catalog { get; } = new();

    public BasketsFactory() => connection.Open();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("CustomerOidc:Issuer", CustomerIssuer);
        builder.UseSetting("CustomerOidc:Audience", "davids-services");
        builder.UseSetting("CustomerOidc:PublicKeyPem", CustomerPublicKeyPem);
        builder.UseSetting("Vault:Enabled", "false");
        builder.UseSetting("Messaging:Enabled", "false");
        builder.UseSetting("Database:ApplyMigrations", "false");
        builder.UseSetting("ConnectionStrings:basketsdb", "unused");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<BasketsDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<BasketsDbContext>>();
            services.RemoveAll<BasketsDbContext>();
            services.AddDbContext<BasketsDbContext>(options => options.UseSqlite(connection));

            services.RemoveAll<IProductCatalogReader>();
            services.AddSingleton<IProductCatalogReader>(Catalog);
        });
    }

    public async Task<HttpClient> CreateBasketsClientAsync()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<BasketsDbContext>().Database.EnsureCreatedAsync();
        return client;
    }

    public static string CreateCustomerToken(Guid sub) => Mint(sub, "user", CustomerIssuer, "davids-services", CustomerKey);
    public static string CreateAdminToken(Guid sub) => Mint(sub, "admin", AdminIssuer, "davids-admin", AdminKey);

    private static string Mint(Guid sub, string role, string issuer, string audience, RsaSecurityKey key)
    {
        var now = DateTime.UtcNow;
        var jwt = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: [new Claim("sub", sub.ToString("N")), new Claim("role", role), new Claim("jti", Guid.NewGuid().ToString("N"))],
            notBefore: now.AddSeconds(-5),
            expires: now.AddMinutes(15),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static RsaSecurityKey CreateKey(RSA rsa)
    {
        var spki = rsa.ExportSubjectPublicKeyInfo();
        var kid = Base64UrlEncoder.Encode(SHA256.HashData(spki))[..16];
        return new RsaSecurityKey(rsa.ExportParameters(true)) { KeyId = kid };
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            connection.Dispose();
        }
    }
}

[Trait("Category", "Integration")]
public sealed class BasketsTests
{
    [Fact]
    public async Task CustomerBasketEnforcesAuthoritativePricingStockLimitsAndPerCustomerIsolation()
    {
        await using var factory = new BasketsFactory();
        var productId = Guid.NewGuid();
        factory.Catalog.Products[productId] = new ProductCatalogSnapshot(productId, "SEC-KEY-01", "Security Key", 54.00m, "GBP", 4, true);

        using var client = await factory.CreateBasketsClientAsync();
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();

        using var addReq = new HttpRequestMessage(HttpMethod.Put, "/baskets/me/items")
        {
            Content = JsonContent.Create(new UpsertBasketItem(productId, 2))
        };
        addReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BasketsFactory.CreateCustomerToken(customerA));
        var addResp = await client.SendAsync(addReq);
        Assert.Equal(HttpStatusCode.OK, addResp.StatusCode);
        var basketA = await addResp.Content.ReadFromJsonAsync<BasketDto>();
        Assert.NotNull(basketA);
        Assert.Equal(108.00m, basketA!.TotalAmount);
        Assert.Equal(2, basketA.TotalItems);

        // Customer B sees an isolated empty basket
        using var getB = new HttpRequestMessage(HttpMethod.Get, "/baskets/me");
        getB.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BasketsFactory.CreateCustomerToken(customerB));
        var respB = await client.SendAsync(getB);
        var basketB = await respB.Content.ReadFromJsonAsync<BasketDto>();
        Assert.NotNull(basketB);
        Assert.Empty(basketB!.Items);

        // Exceeding stock returns 409 Conflict
        using var overStockReq = new HttpRequestMessage(HttpMethod.Put, "/baskets/me/items")
        {
            Content = JsonContent.Create(new UpsertBasketItem(productId, 10))
        };
        overStockReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BasketsFactory.CreateCustomerToken(customerA));
        var overResp = await client.SendAsync(overStockReq);
        Assert.Equal(HttpStatusCode.Conflict, overResp.StatusCode);

        // Workforce token rejected on CustomerOnly endpoint
        using var adminReq = new HttpRequestMessage(HttpMethod.Get, "/baskets/me");
        adminReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BasketsFactory.CreateAdminToken(Guid.NewGuid()));
        var adminResp = await client.SendAsync(adminReq);
        Assert.Equal(HttpStatusCode.Unauthorized, adminResp.StatusCode);

        // Adding a second distinct item, updating quantity, and removing an item persist cleanly across requests
        var secondProductId = Guid.NewGuid();
        factory.Catalog.Products[secondProductId] = new ProductCatalogSnapshot(secondProductId, "DEV-DOCK-02", "Workstation Dock", 200.00m, "GBP", 10, true);

        using var addSecondReq = new HttpRequestMessage(HttpMethod.Put, "/baskets/me/items")
        {
            Content = JsonContent.Create(new UpsertBasketItem(secondProductId, 1))
        };
        addSecondReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BasketsFactory.CreateCustomerToken(customerA));
        var addSecondResp = await client.SendAsync(addSecondReq);
        Assert.Equal(HttpStatusCode.OK, addSecondResp.StatusCode);
        var multiBasket = await addSecondResp.Content.ReadFromJsonAsync<BasketDto>();
        Assert.NotNull(multiBasket);
        Assert.Equal(2, multiBasket!.Items.Count);
        Assert.Equal(308.00m, multiBasket.TotalAmount);

        using var updateFirstReq = new HttpRequestMessage(HttpMethod.Put, "/baskets/me/items")
        {
            Content = JsonContent.Create(new UpsertBasketItem(productId, 3))
        };
        updateFirstReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BasketsFactory.CreateCustomerToken(customerA));
        var updateFirstResp = await client.SendAsync(updateFirstReq);
        Assert.Equal(HttpStatusCode.OK, updateFirstResp.StatusCode);
        var updatedBasket = await updateFirstResp.Content.ReadFromJsonAsync<BasketDto>();
        Assert.NotNull(updatedBasket);
        Assert.Equal(362.00m, updatedBasket!.TotalAmount);

        using var removeReq = new HttpRequestMessage(HttpMethod.Delete, $"/baskets/me/items/{secondProductId}");
        removeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BasketsFactory.CreateCustomerToken(customerA));
        var removeResp = await client.SendAsync(removeReq);
        Assert.Equal(HttpStatusCode.OK, removeResp.StatusCode);
        var afterRemove = await removeResp.Content.ReadFromJsonAsync<BasketDto>();
        Assert.NotNull(afterRemove);
        Assert.Single(afterRemove!.Items);
        Assert.Equal(162.00m, afterRemove.TotalAmount);
    }

    [Fact]
    public void AddBasketsInfrastructureSucceedsWithRealAppsettingsAndAppHostEnvironmentOnly()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var appSettingsPath = Path.Combine(repoRoot, "src", "Davids.Services.Baskets.Api", "appsettings.json");
        Assert.True(File.Exists(appSettingsPath), $"Expected appsettings.json at {appSettingsPath}");

        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddJsonFile(appSettingsPath, optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:basketsdb"] = "Server=127.0.0.1,1433;Database=basketsdb;User Id=sa;Password=TestOnlyPassword123!;TrustServerCertificate=True",
                ["ConnectionStrings:messaging"] = "amqp://guest:guest@127.0.0.1:5672",
                ["CustomerOidc:Authority"] = "http://localhost:5004",
                ["CustomerOidc:Issuer"] = "http://localhost:5004/",
                ["ProductsApi:BaseUrl"] = "http://localhost:5006/",
                ["Database:ApplyMigrations"] = "true",
                ["Messaging:Enabled"] = "true"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        Davids.Services.Baskets.Infrastructure.BasketsInfrastructureExtensions.AddBasketsInfrastructure(services, config);

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IBasketsService>());
    }
}
