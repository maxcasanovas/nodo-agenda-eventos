using Nodo.BuildingBlocks.Application;
using Nodo.EventLifecycle.Contracts;
using Nodo.HallSchedule.Domain;

namespace Nodo.HallSchedule.Application;

/// <summary>Frees an event's slot. Idempotent: releasing a missing or already released block does nothing.</summary>
internal sealed class SlotReleaser(IHallScheduleRepository schedules, IHallScheduleUnitOfWork unitOfWork, IClock clock)
{
    public async Task ReleaseAsync(Guid hallId, DateOnly date, Guid eventId, CancellationToken cancellationToken)
    {
        var schedule = await schedules.GetOrCreateAsync(new HallId(hallId), date, cancellationToken);
        schedule.Release(new EventId(eventId), clock.UtcNow);

        if (schedule.Blocks.Count == 0)
            return; // nothing was ever placed for this day

        await schedules.SaveAsync(schedule, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

// Consistency between modules is eventual: confirming consolidates the block, rejecting/expiring/cancelling releases it.

internal sealed class HallEventFirmlyConfirmedHandler(
    IHallScheduleRepository schedules, IHallScheduleUnitOfWork unitOfWork, IClock clock)
    : IIntegrationEventHandler<HallEventFirmlyConfirmedIntegrationEvent>
{
    public async Task HandleAsync(
        HallEventFirmlyConfirmedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var schedule = await schedules.GetOrCreateAsync(
            new HallId(integrationEvent.HallId), integrationEvent.Date, cancellationToken);
        schedule.Consolidate(new EventId(integrationEvent.EventId), clock.UtcNow);

        if (schedule.Blocks.Count == 0)
            return;

        await schedules.SaveAsync(schedule, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class EventRequestRejectedHandler(SlotReleaser releaser)
    : IIntegrationEventHandler<EventRequestRejectedIntegrationEvent>
{
    public Task HandleAsync(EventRequestRejectedIntegrationEvent e, CancellationToken cancellationToken = default) =>
        releaser.ReleaseAsync(e.HallId, e.Date, e.EventId, cancellationToken);
}

internal sealed class HallEventExpiredHandler(SlotReleaser releaser)
    : IIntegrationEventHandler<HallEventExpiredIntegrationEvent>
{
    public Task HandleAsync(HallEventExpiredIntegrationEvent e, CancellationToken cancellationToken = default) =>
        releaser.ReleaseAsync(e.HallId, e.Date, e.EventId, cancellationToken);
}

internal sealed class HallEventCancelledWithCreditHandler(SlotReleaser releaser)
    : IIntegrationEventHandler<HallEventCancelledWithCreditIntegrationEvent>
{
    public Task HandleAsync(HallEventCancelledWithCreditIntegrationEvent e, CancellationToken cancellationToken = default) =>
        releaser.ReleaseAsync(e.HallId, e.Date, e.EventId, cancellationToken);
}
