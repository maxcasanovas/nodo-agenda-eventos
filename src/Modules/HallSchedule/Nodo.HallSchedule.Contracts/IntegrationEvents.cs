using Nodo.BuildingBlocks.Application;

namespace Nodo.HallSchedule.Contracts;

public sealed record SlotProvisionallyBlockedIntegrationEvent(
    Guid Id, DateTime OccurredOnUtc, Guid EventId, Guid HallId, DateOnly Date, TimeOnly Start, TimeOnly End)
    : IIntegrationEvent;

public sealed record SlotBlockConsolidatedIntegrationEvent(
    Guid Id, DateTime OccurredOnUtc, Guid EventId, Guid HallId, DateOnly Date, TimeOnly Start, TimeOnly End)
    : IIntegrationEvent;

public sealed record SlotReleasedIntegrationEvent(
    Guid Id, DateTime OccurredOnUtc, Guid EventId, Guid HallId, DateOnly Date, TimeOnly Start, TimeOnly End)
    : IIntegrationEvent;
