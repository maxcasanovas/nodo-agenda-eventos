namespace Nodo.SharedKernel;

/// <summary>Non-generic view of an aggregate root, so infrastructure can collect domain events from tracked entities.</summary>
public interface IAggregateRoot
{
    IReadOnlyList<IDomainEvent> DequeueDomainEvents();
}

public abstract class AggregateRoot<TId> : Entity<TId>, IAggregateRoot
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id) : base(id)
    {
    }

    protected void RaiseDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>Returns the pending domain events and clears the internal list.</summary>
    public IReadOnlyList<IDomainEvent> DequeueDomainEvents()
    {
        var events = _domainEvents.ToArray();
        _domainEvents.Clear();
        return events;
    }
}
