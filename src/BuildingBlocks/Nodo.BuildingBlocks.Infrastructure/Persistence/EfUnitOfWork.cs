using Microsoft.EntityFrameworkCore;
using Nodo.BuildingBlocks.Application;
using Nodo.BuildingBlocks.Infrastructure.Messaging;
using Nodo.SharedKernel;

namespace Nodo.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// One transaction per module: (1) dispatches the domain events of the tracked aggregates until none are left, so their
/// handlers can add integration events to the outbox and even raise more events; (2) a single SaveChanges commits
/// aggregates and outbox together. Database errors that mean "an invariant was violated" become 409 conflicts.
/// </summary>
public class EfUnitOfWork<TContext>(
    TContext context,
    IDomainEventDispatcher dispatcher,
    InvariantConstraintMap constraints) : IUnitOfWork
    where TContext : DbContext, IMessagingDbContext
{
    public bool HasPendingChanges => context.ChangeTracker.HasChanges();

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            while (CollectDomainEvents() is { Count: > 0 } domainEvents)
                await dispatcher.DispatchAsync(domainEvents, cancellationToken);

            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Otra operación modificó este registro al mismo tiempo. Vuelve a intentarlo.");
        }
        catch (DbUpdateException ex) when (constraints.Translate(ex) is { } conflict)
        {
            throw conflict;
        }
    }

    private List<IDomainEvent> CollectDomainEvents() => context.ChangeTracker
        .Entries<IAggregateRoot>()
        .Select(entry => entry.Entity)
        .ToList()
        .SelectMany(aggregate => aggregate.DequeueDomainEvents())
        .ToList();
}
