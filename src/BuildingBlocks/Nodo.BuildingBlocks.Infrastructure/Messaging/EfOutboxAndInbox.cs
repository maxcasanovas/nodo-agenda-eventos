using Microsoft.EntityFrameworkCore;
using Nodo.BuildingBlocks.Application;

namespace Nodo.BuildingBlocks.Infrastructure.Messaging;

/// <summary>Adds integration events to the module's DbContext: they commit with the aggregates (Transactional Outbox).</summary>
public class EfOutbox<TContext>(TContext context, IntegrationEventSerializer serializer) : IOutbox
    where TContext : DbContext, IMessagingDbContext
{
    public void Add(IIntegrationEvent integrationEvent)
    {
        var (type, content) = serializer.Serialize(integrationEvent);
        context.OutboxMessages.Add(new OutboxMessage
        {
            Id = integrationEvent.Id,
            Type = type,
            Content = content,
            OccurredOnUtc = integrationEvent.OccurredOnUtc
        });
    }
}

/// <summary>Registers handled messages in the consumer module's DbContext, to commit with its work.</summary>
public class EfInbox<TContext>(TContext context, IClock clock) : IInbox
    where TContext : DbContext, IMessagingDbContext
{
    public async Task<bool> TryRegisterAsync(Guid messageId, string consumer, CancellationToken cancellationToken = default)
    {
        var alreadyRegistered =
            context.InboxMessages.Local.Any(m => m.MessageId == messageId && m.Handler == consumer) ||
            await context.InboxMessages.AnyAsync(m => m.MessageId == messageId && m.Handler == consumer, cancellationToken);
        if (alreadyRegistered)
            return false;

        context.InboxMessages.Add(new InboxMessage { MessageId = messageId, Handler = consumer, ProcessedOnUtc = clock.UtcNow });
        return true;
    }
}
