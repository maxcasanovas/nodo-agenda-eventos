using Nodo.BuildingBlocks.Application;
using Nodo.HallSchedule.Contracts;
using Nodo.HallSchedule.Domain;

namespace Nodo.HallSchedule.Application;

// Domain event -> integration event. Explicit mapping: the integration event is the public contract.

internal sealed class SlotProvisionallyBlockedMapper(IHallScheduleOutbox outbox) : DomainEventHandler<SlotProvisionallyBlocked>
{
    protected override Task HandleAsync(SlotProvisionallyBlocked e, CancellationToken cancellationToken)
    {
        outbox.Add(new SlotProvisionallyBlockedIntegrationEvent(
            Guid.NewGuid(), e.OccurredOnUtc, e.EventId.Value, e.HallId.Value, e.Date, e.Start, e.End));
        return Task.CompletedTask;
    }
}

internal sealed class SlotBlockConsolidatedMapper(IHallScheduleOutbox outbox) : DomainEventHandler<SlotBlockConsolidated>
{
    protected override Task HandleAsync(SlotBlockConsolidated e, CancellationToken cancellationToken)
    {
        outbox.Add(new SlotBlockConsolidatedIntegrationEvent(
            Guid.NewGuid(), e.OccurredOnUtc, e.EventId.Value, e.HallId.Value, e.Date, e.Start, e.End));
        return Task.CompletedTask;
    }
}

internal sealed class SlotReleasedMapper(IHallScheduleOutbox outbox) : DomainEventHandler<SlotReleased>
{
    protected override Task HandleAsync(SlotReleased e, CancellationToken cancellationToken)
    {
        outbox.Add(new SlotReleasedIntegrationEvent(
            Guid.NewGuid(), e.OccurredOnUtc, e.EventId.Value, e.HallId.Value, e.Date, e.Start, e.End));
        return Task.CompletedTask;
    }
}
