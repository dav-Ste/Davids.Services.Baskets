namespace Davids.Services.Baskets.Application.Events;

public sealed record OrderLifecycleCustomerEvent(Guid OrderId, Guid CustomerId);
