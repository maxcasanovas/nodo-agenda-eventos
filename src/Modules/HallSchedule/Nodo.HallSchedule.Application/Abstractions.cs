using Nodo.BuildingBlocks.Application;
using Nodo.HallSchedule.Contracts;

namespace Nodo.HallSchedule.Application;

public interface IHallScheduleUnitOfWork : IUnitOfWork;

public interface IHallScheduleOutbox : IOutbox;

public interface IHallScheduleInbox : IInbox;

/// <summary>Upstream "Espacios y Recursos" as seen by HallSchedule: only the own-equipment stock (Customer/Supplier, OHS/PL).</summary>
public interface ISpacesCatalog
{
    /// <returns>Stock by equipment code.</returns>
    Task<IReadOnlyDictionary<string, int>> GetEquipmentStockAsync(CancellationToken cancellationToken = default);
}

/// <summary>Raw active block as stored; the query handler adds buffers and event/customer information.</summary>
public sealed record ScheduleBlockReadModel(
    Guid BlockId, Guid EventId, DateOnly Date, TimeOnly Start, TimeOnly End, string Kind,
    IReadOnlyCollection<EquipmentRequest> Equipment);

/// <summary>Read side implemented over the database with AsNoTracking (spec 04).</summary>
public interface IHallCalendarQueries
{
    Task<IReadOnlyCollection<ScheduleBlockReadModel>> GetActiveBlocksAsync(
        Guid hallId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
