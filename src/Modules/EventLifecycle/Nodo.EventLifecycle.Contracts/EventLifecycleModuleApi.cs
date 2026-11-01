namespace Nodo.EventLifecycle.Contracts;

/// <summary>Minimal public view of an event, for other modules' read models (e.g. the hall calendar).</summary>
public sealed record EventSummary(Guid EventId, Guid CustomerId, string? CustomerName, string Status);

/// <summary>Synchronous read API published by EventLifecycle.</summary>
public interface IEventLifecycleModuleApi
{
    Task<IReadOnlyCollection<EventSummary>> GetEventSummariesAsync(
        IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken = default);
}
