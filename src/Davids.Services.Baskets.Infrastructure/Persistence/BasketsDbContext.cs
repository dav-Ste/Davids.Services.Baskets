using Davids.Services.Baskets.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Davids.Services.Baskets.Infrastructure.Persistence;

public sealed class BasketsDbContext(DbContextOptions<BasketsDbContext> options) : DbContext(options)
{
    public DbSet<CustomerBasket> Baskets => Set<CustomerBasket>();
    public DbSet<BasketItem> BasketItems => Set<BasketItem>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<InboxMessage> Inbox => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CustomerBasket>(entity =>
        {
            entity.ToTable("CustomerBaskets");
            entity.HasKey(x => x.CustomerId);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.HasMany(x => x.Items)
                .WithOne()
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<BasketItem>(entity =>
        {
            entity.ToTable("BasketItems");
            entity.HasKey(x => new { x.CustomerId, x.ProductId });
            entity.Property(x => x.Sku).HasMaxLength(32).IsRequired();
            entity.Property(x => x.ProductName).HasMaxLength(120).IsRequired();
            entity.Property(x => x.UnitPrice).HasPrecision(18, 2).IsRequired();
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("Outbox");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(64);
            entity.Property(x => x.Type).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Payload).IsRequired();
            entity.HasIndex(x => x.PublishedAt);
        });

        modelBuilder.Entity<InboxMessage>(entity =>
        {
            entity.ToTable("Inbox");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(128);
        });
    }
}

public sealed class OutboxMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
}

public sealed class InboxMessage
{
    public string Id { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; }
}
