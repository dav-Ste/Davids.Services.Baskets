using System.Text.Json;
using Davids.Services.Baskets.Application.Events;
using Davids.Services.Baskets.Application.Services;
using Davids.Services.Baskets.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Davids.Services.Baskets.Infrastructure.Messaging;

public sealed class BasketsMessageWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<BasketsMessageWorker> logger) : BackgroundService
{
    private const string OrderPlacedQueue = "baskets-service/orders.order_placed";
    private const string OrderCompletedQueue = "baskets-service/orders.order_completed";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Baskets RabbitMQ worker disconnected; retrying.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var connectionString = configuration.GetConnectionString("messaging")
            ?? throw new InvalidOperationException("Missing RabbitMQ messaging connection string.");
        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
        await using var connection = await factory.CreateConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);

        await channel.ExchangeDeclareAsync("orders", ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.ExchangeDeclareAsync("baskets.dead", ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.QueueDeclareAsync("baskets-service.dead", durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync("baskets-service.dead", "baskets.dead", "#", cancellationToken: ct);

        var dlxArgs = new Dictionary<string, object?> { ["x-dead-letter-exchange"] = "baskets.dead" };
        await channel.QueueDeclareAsync(OrderPlacedQueue, durable: true, exclusive: false, autoDelete: false, arguments: dlxArgs, cancellationToken: ct);
        await channel.QueueBindAsync(OrderPlacedQueue, "orders", "order_placed", cancellationToken: ct);
        await channel.QueueDeclareAsync(OrderCompletedQueue, durable: true, exclusive: false, autoDelete: false, arguments: dlxArgs, cancellationToken: ct);
        await channel.QueueBindAsync(OrderCompletedQueue, "orders", "order_completed", cancellationToken: ct);
        await channel.BasicQosAsync(0, 1, global: false, ct);

        async Task ConsumeAsync(BasicDeliverEventArgs args)
        {
            var messageId = args.BasicProperties.MessageId;
            if (string.IsNullOrWhiteSpace(messageId))
            {
                messageId = $"{args.RoutingKey}:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(args.Body.Span))[..24]}";
            }

            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BasketsDbContext>();
                if (!await db.Inbox.AnyAsync(x => x.Id == messageId, ct))
                {
                    var evt = JsonSerializer.Deserialize<OrderLifecycleCustomerEvent>(args.Body.Span, JsonOptions);
                    if (evt is not null && evt.CustomerId != Guid.Empty)
                    {
                        var svc = scope.ServiceProvider.GetRequiredService<IBasketsService>();
                        await svc.ClearBasketAsync(evt.CustomerId, ct);
                    }

                    db.Inbox.Add(new InboxMessage { Id = messageId, ReceivedAt = DateTime.UtcNow });
                    await db.SaveChangesAsync(ct);
                }

                await channel.BasicAckAsync(args.DeliveryTag, multiple: false, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed processing {RoutingKey} in Baskets.", args.RoutingKey);
                await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, ct);
            }
        }

        var placedConsumer = new AsyncEventingBasicConsumer(channel);
        placedConsumer.ReceivedAsync += (_, args) => ConsumeAsync(args);
        await channel.BasicConsumeAsync(OrderPlacedQueue, autoAck: false, placedConsumer, ct);

        var completedConsumer = new AsyncEventingBasicConsumer(channel);
        completedConsumer.ReceivedAsync += (_, args) => ConsumeAsync(args);
        await channel.BasicConsumeAsync(OrderCompletedQueue, autoAck: false, completedConsumer, ct);

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }
}
