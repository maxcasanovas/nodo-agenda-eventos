using Nodo.SharedKernel;

namespace Nodo.HallSchedule.Domain;

public sealed record SlotProvisionallyBlocked(
    EventId EventId, HallId HallId, DateOnly Date, TimeOnly Start, TimeOnly End, DateTime OccurredOnUtc) : IDomainEvent;

public sealed record SlotBlockConsolidated(
    EventId EventId, HallId HallId, DateOnly Date, TimeOnly Start, TimeOnly End, DateTime OccurredOnUtc) : IDomainEvent;

public sealed record SlotReleased(
    EventId EventId, HallId HallId, DateOnly Date, TimeOnly Start, TimeOnly End, DateTime OccurredOnUtc) : IDomainEvent;
