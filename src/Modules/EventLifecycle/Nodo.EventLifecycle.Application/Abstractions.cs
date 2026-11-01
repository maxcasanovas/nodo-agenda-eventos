using Nodo.BuildingBlocks.Application;
using Nodo.EventLifecycle.Contracts;

namespace Nodo.EventLifecycle.Application;

public interface IEventLifecycleUnitOfWork : IUnitOfWork;

public interface IEventLifecycleOutbox : IOutbox;

public interface IEventLifecycleInbox : IInbox;

/// <summary>Upstream "Espacios y Recursos" as seen by EventLifecycle: only the own-equipment stock (BR-08).</summary>
public interface ISpacesCatalog
{
    /// <returns>Stock by equipment code.</returns>
    Task<IReadOnlyDictionary<string, int>> GetEquipmentStockAsync(CancellationToken cancellationToken = default);
}

public interface ICustomerDirectory
{
    Task<string?> GetNameAsync(Guid customerId, CancellationToken cancellationToken = default);
}

/// <summary>Read side implemented over the database with AsNoTracking (spec 04). Returns DTOs without the customer name.</summary>
public interface IHallEventQueries
{
    Task<HallEventDto?> GetAsync(Guid eventId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<HallEventListItemDto>> ListAsync(
        string? status, Guid? customerId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default);

    /// <summary>Requested events still waiting for Management.</summary>
    Task<IReadOnlyCollection<HallEventListItemDto>> GetPendingApprovalsAsync(CancellationToken cancellationToken = default);

    /// <summary>Confirmed or in-progress events whose local date is <paramref name="todayLocal"/>.</summary>
    Task<IReadOnlyCollection<HallEventListItemDto>> GetTodayOperationsAsync(
        DateOnly todayLocal, CancellationToken cancellationToken = default);

    Task<CustomerCreditDto?> GetCreditAsync(Guid customerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<EventSummary>> GetSummariesAsync(
        IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken = default);
}
