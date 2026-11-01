using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodo.BuildingBlocks.Application;
using Nodo.SharedKernel;

namespace Nodo.TestDoubles;

// Linked into every module test project (tests/Shared). In-memory stand-ins for what Infrastructure provides in spec 04.

public sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    public DateTime LocalNow => NodoTimeZone.ToLocal(UtcNow);

    public DateOnly TodayLocal => DateOnly.FromDateTime(LocalNow);

    public void Advance(TimeSpan amount) => UtcNow = UtcNow.Add(amount);
}

public class InMemoryOutbox : IOutbox
{
    private readonly List<IIntegrationEvent> _events = [];

    public IReadOnlyList<IIntegrationEvent> Events => _events;

    public void Add(IIntegrationEvent integrationEvent) => _events.Add(integrationEvent);

    public T Single<T>() where T : IIntegrationEvent => _events.OfType<T>().Single();

    public int Count<T>() where T : IIntegrationEvent => _events.OfType<T>().Count();

    public void Clear() => _events.Clear();
}

public class InMemoryInbox(AggregateTracker tracker) : IInbox
{
    private readonly HashSet<(Guid, string)> _processed = [];

    public Task<bool> TryRegisterAsync(Guid messageId, string consumer, CancellationToken cancellationToken = default)
    {
        var isNew = _processed.Add((messageId, consumer));
        if (isNew)
            tracker.MarkDirty(); // like an unsaved inbox row in the DbContext
        return Task.FromResult(isNew);
    }
}

/// <summary>Remembers the aggregates a test touched, like EF's change tracker, so their domain events can be dispatched.</summary>
public sealed class AggregateTracker
{
    private readonly List<Func<IReadOnlyList<IDomainEvent>>> _sources = [];
    private readonly HashSet<object> _tracked = [];

    /// <summary>True when something was tracked or registered since the last commit.</summary>
    public bool Dirty { get; private set; }

    public void Track<TId>(AggregateRoot<TId> aggregate) where TId : notnull
    {
        Dirty = true;
        if (_tracked.Add(aggregate))
            _sources.Add(aggregate.DequeueDomainEvents);
    }

    public void MarkDirty() => Dirty = true;

    public void Committed() => Dirty = false;

    public IReadOnlyList<IDomainEvent> DequeueAll() => _sources.SelectMany(s => s()).ToList();
}

/// <summary>
/// Same contract as the real unit of work: dispatches domain events (and the events their handlers raise)
/// before "committing", and counts commits so tests can assert that nothing was saved.
/// </summary>
public class TrackingUnitOfWork(IDomainEventDispatcher dispatcher, AggregateTracker tracker) : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public bool HasPendingChanges => tracker.Dirty;

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        while (tracker.DequeueAll() is { Count: > 0 } events)
            await dispatcher.DispatchAsync(events, cancellationToken);

        SaveCount++;
        tracker.Committed();
    }
}

public static class TestContainer
{
    /// <summary>Real MediatR (handlers scanned from <paramref name="applicationAssembly"/>) plus the real domain event dispatcher.</summary>
    public static IServiceCollection CreateServices(System.Reflection.Assembly applicationAssembly)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(applicationAssembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        services.AddSingleton<IDomainEventDispatcher, MediatRDomainEventDispatcher>();
        services.AddSingleton<AggregateTracker>();
        services.AddSingleton<FakeClock>();
        services.AddSingleton<IClock>(sp => sp.GetRequiredService<FakeClock>());
        return services;
    }

    /// <summary>Every domain event type of <paramref name="domainAssembly"/> must have at least one application handler.</summary>
    public static IEnumerable<Type> DomainEventTypes(System.Reflection.Assembly domainAssembly) =>
        domainAssembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false } && t.IsAssignableTo(typeof(IDomainEvent)));

    public static int HandlerCount(IServiceProvider provider, Type domainEventType)
    {
        var notification = typeof(DomainEventNotification<>).MakeGenericType(domainEventType);
        var handler = typeof(INotificationHandler<>).MakeGenericType(notification);
        return provider.GetServices(handler).Count();
    }
}
