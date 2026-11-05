using Davids.Services.Baskets.Core.Entities;
using Davids.Services.Baskets.Core.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Davids.Services.Baskets.Infrastructure.Persistence;

public sealed class CustomerBasketRepository(BasketsDbContext db) : ICustomerBasketRepository
{
    public Task<CustomerBasket?> GetAsync(Guid customerId, CancellationToken ct = default)
        => db.Baskets.Include(x => x.Items).FirstOrDefaultAsync(x => x.CustomerId == customerId, ct);

    public Task AddAsync(CustomerBasket basket, CancellationToken ct = default)
        => db.Baskets.AddAsync(basket, ct).AsTask();

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
