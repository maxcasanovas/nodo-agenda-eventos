using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nodo.BuildingBlocks.Application;

namespace Nodo.BuildingBlocks.Infrastructure.Messaging;

/// <summary>
/// Delivers the module's pending outbox messages to the in-memory bus: every second, up to 20 at a time, locking rows
/// with FOR UPDATE SKIP LOCKED so several instances never deliver the same message. At-least-once delivery;
/// a message that keeps failing is parked after <see cref="OutboxMessage.MaxAttempts"/> attempts.
/// </summary>
public sealed class OutboxProcessor<TContext>(
    IServiceScopeFactory scopeFactory,
    IntegrationEventSerializer serializer,
    IEventBus eventBus,
    ILogger<OutboxProcessor<TContext>> logger) : BackgroundService
    where TContext : DbContext, IMessagingDbContext
{
    public const int BatchSize = 20;
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // e.g. the database is not reachable yet: try again on the next tick.
                logger.LogWarning(ex, "Outbox polling of {Context} failed", typeof(TContext).Name);
            }

            await Task.Delay(PollingInterval, stoppingToken);
        }
    }

    /// <returns>How many messages were delivered.</returns>
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var entityType = context.Model.FindEntityType(typeof(OutboxMessage))!;
        var table = $"\"{entityType.GetSchema()}\".\"{entityType.GetTableName()}\"";

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // Identifiers cannot be parameters. Schema and table come from EF's own model and the numbers are constants:
        // nothing here is external input.
#pragma warning disable EF1003
        var pending = await context.OutboxMessages
            .FromSqlRaw(
                $"SELECT * FROM {table} WHERE processed_on_utc IS NULL AND attempts < {OutboxMessage.MaxAttempts} " +
                $"ORDER BY occurred_on_utc LIMIT {BatchSize} FOR UPDATE SKIP LOCKED")
            .ToListAsync(cancellationToken);
#pragma warning restore EF1003

        var delivered = 0;
        foreach (var message in pending)
        {
            try
            {
                var integrationEvent = serializer.Deserialize(message.Type, message.Content);
                await eventBus.PublishAsync(integrationEvent, cancellationToken);
                message.ProcessedOnUtc = clock.UtcNow;
                message.Error = null;
                delivered++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.Attempts++;
                message.Error = Truncate(ex.Message);
                logger.LogWarning(ex, "Outbox message {MessageId} ({Type}) failed, attempt {Attempt}",
                    message.Id, message.Type, message.Attempts);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return delivered;
    }

    private static string Truncate(string text) => text.Length <= 1900 ? text : text[..1900];
}
