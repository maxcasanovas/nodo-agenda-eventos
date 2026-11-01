namespace Nodo.BuildingBlocks.Application;

// Ports implemented by each module's Infrastructure (spec 04). Every module declares its own marker interface
// (e.g. IQuotesUnitOfWork : IUnitOfWork) so that it writes to its own DbContext and schema.

/// <summary>
/// Commits the module's changes in one transaction. Implementations dispatch the pending domain events
/// (through <see cref="IDomainEventDispatcher"/>) before committing, so their handlers write to the outbox in the same transaction.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>True when something was tracked or registered and not committed yet.</summary>
    bool HasPendingChanges { get; }

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Collects integration events to be persisted with the current transaction (Transactional Outbox).</summary>
public interface IOutbox
{
    void Add(IIntegrationEvent integrationEvent);
}

/// <summary>Idempotency for integration event consumers: a message is processed once per consumer.</summary>
public interface IInbox
{
    /// <summary>
    /// Registers the message in the module's database context without committing: the consumer's own
    /// <see cref="IUnitOfWork"/> commit makes the registration atomic with its work.
    /// </summary>
    /// <returns>False when the consumer already processed this message.</returns>
    Task<bool> TryRegisterAsync(Guid messageId, string consumer, CancellationToken cancellationToken = default);
}

/// <summary>
/// Makes any integration event handler idempotent: skips messages already processed by this consumer and commits the
/// inbox registration together with the handler's work (or by itself if the handler had nothing to save).
/// A handler that throws leaves nothing registered, so the message is retried.
/// </summary>
public sealed class InboxIntegrationEventHandler<TEvent>(
    IIntegrationEventHandler<TEvent> inner, IInbox inbox, IUnitOfWork unitOfWork, string consumer)
    : IIntegrationEventHandler<TEvent>
    where TEvent : IIntegrationEvent
{
    public async Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (!await inbox.TryRegisterAsync(integrationEvent.Id, consumer, cancellationToken))
            return;

        await inner.HandleAsync(integrationEvent, cancellationToken);

        if (unitOfWork.HasPendingChanges)
            await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
