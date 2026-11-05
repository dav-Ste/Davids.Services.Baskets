using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Davids.Services.Baskets.Infrastructure.Persistence;

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<BasketsDbContext>
{
    public BasketsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BasketsDbContext>()
            .UseSqlServer("Server=localhost;Database=basketsdb;TrustServerCertificate=True")
            .Options;
        return new BasketsDbContext(options);
    }
}
