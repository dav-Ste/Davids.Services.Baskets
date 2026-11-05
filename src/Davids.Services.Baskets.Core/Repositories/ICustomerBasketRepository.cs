using Davids.Services.Baskets.Core.Entities;

namespace Davids.Services.Baskets.Core.Repositories;

public interface ICustomerBasketRepository
{
    Task<CustomerBasket?> GetAsync(Guid customerId, CancellationToken ct = default);
    Task AddAsync(CustomerBasket basket, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
