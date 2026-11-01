using Nodo.EventLifecycle.Contracts;
using Nodo.HallSchedule.Contracts;
using Nodo.HallSchedule.Domain;
using static Nodo.HallSchedule.UnitTests.UseCases.HsTestHost;

namespace Nodo.HallSchedule.UnitTests.UseCases;

/// <summary>Reactions of HallSchedule to EventLifecycle's integration events (eventual consistency, idempotent).</summary>
public class IntegrationHandlerTests
{
    private static readonly Guid EventId = Guid.Parse("44444444-0000-0000-0000-000000000001");
    private static readonly DateTime When = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private static HallEventFirmlyConfirmedIntegrationEvent Confirmed() => new(
        Guid.NewGuid(), When, EventId, HallGuid, Day, new TimeOnly(10, 0), new TimeOnly(14, 0), 40,
        Guid.NewGuid(), 2000m, 600m, []);

    private static EventRequestRejectedIntegrationEvent Rejected() => new(
        Guid.NewGuid(), When, EventId, HallGuid, Day, "Lucía Paz", "Motivo suficientemente largo");

    private static HallEventExpiredIntegrationEvent Expired() => new(
        Guid.NewGuid(), When, EventId, HallGuid, Day, Guid.NewGuid(), 0m);

    private static HallEventCancelledWithCreditIntegrationEvent Cancelled() => new(
        Guid.NewGuid(), When, EventId, HallGuid, Day, Guid.NewGuid(), 600m);

    private static async Task<HsTestHost> HostWithProvisionalBlockAsync()
    {
        var host = new HsTestHost();
        (await host.PlaceProvisionalAsync(EventId, 10, 14)).Success.ShouldBeTrue();
        host.Outbox.Clear();
        return host;
    }

    private static BlockKind KindOfBlock(HsTestHost host) =>
        host.Schedules.Peek(new HallId(HallGuid), Day)!.Blocks.Single().Kind;

    // ---- HallEventFirmlyConfirmed => Consolidate ----

    [Fact]
    public async Task FirmlyConfirmedHandler_ProvisionalBlock_BecomesDefinitiveAndPublishesConsolidated()
    {
        using var host = await HostWithProvisionalBlockAsync();

        await host.IntegrationHandler<HallEventFirmlyConfirmedIntegrationEvent>().HandleAsync(Confirmed());

        KindOfBlock(host).ShouldBe(BlockKind.Definitive);
        host.Outbox.Single<SlotBlockConsolidatedIntegrationEvent>().EventId.ShouldBe(EventId);
    }

    [Fact]
    public async Task FirmlyConfirmedHandler_ProcessedTwice_IsIdempotent()
    {
        using var host = await HostWithProvisionalBlockAsync();
        var handler = host.IntegrationHandler<HallEventFirmlyConfirmedIntegrationEvent>();
        var message = Confirmed();

        await handler.HandleAsync(message);
        await handler.HandleAsync(message);

        KindOfBlock(host).ShouldBe(BlockKind.Definitive);
        host.Outbox.Count<SlotBlockConsolidatedIntegrationEvent>().ShouldBe(1);
    }

    [Fact]
    public async Task FirmlyConfirmedHandler_NoBlockForThatDay_DoesNothing()
    {
        using var host = new HsTestHost();

        await host.IntegrationHandler<HallEventFirmlyConfirmedIntegrationEvent>().HandleAsync(Confirmed());

        host.UnitOfWork.SaveCount.ShouldBe(0);
        host.Schedules.Contains(new HallId(HallGuid), Day).ShouldBeFalse();
        host.Outbox.Events.ShouldBeEmpty();
    }

    // ---- EventRequestRejected / HallEventExpired / HallEventCancelledWithCredit => Release ----

    [Fact]
    public async Task RejectedHandler_ActiveBlock_ReleasesItAndPublishesSlotReleased()
    {
        using var host = await HostWithProvisionalBlockAsync();

        await host.IntegrationHandler<EventRequestRejectedIntegrationEvent>().HandleAsync(Rejected());

        host.Schedules.Peek(new HallId(HallGuid), Day)!.ActiveBlocks.ShouldBeEmpty();
        host.Outbox.Single<SlotReleasedIntegrationEvent>().EventId.ShouldBe(EventId);
    }

    [Fact]
    public async Task ExpiredHandler_ActiveBlock_ReleasesIt()
    {
        using var host = await HostWithProvisionalBlockAsync();

        await host.IntegrationHandler<HallEventExpiredIntegrationEvent>().HandleAsync(Expired());

        host.Schedules.Peek(new HallId(HallGuid), Day)!.ActiveBlocks.ShouldBeEmpty();
        host.Outbox.Count<SlotReleasedIntegrationEvent>().ShouldBe(1);
    }

    [Fact]
    public async Task CancelledHandler_ConfirmedBlock_ReleasesIt()
    {
        using var host = await HostWithProvisionalBlockAsync();
        await host.IntegrationHandler<HallEventFirmlyConfirmedIntegrationEvent>().HandleAsync(Confirmed());
        host.Outbox.Clear();

        await host.IntegrationHandler<HallEventCancelledWithCreditIntegrationEvent>().HandleAsync(Cancelled());

        host.Schedules.Peek(new HallId(HallGuid), Day)!.ActiveBlocks.ShouldBeEmpty();
        host.Outbox.Count<SlotReleasedIntegrationEvent>().ShouldBe(1);
    }

    [Fact]
    public async Task ReleaseHandlers_ProcessedTwice_AreIdempotent()
    {
        using var host = await HostWithProvisionalBlockAsync();
        var handler = host.IntegrationHandler<HallEventCancelledWithCreditIntegrationEvent>();
        var message = Cancelled();

        await handler.HandleAsync(message);
        await handler.HandleAsync(message);

        host.Outbox.Count<SlotReleasedIntegrationEvent>().ShouldBe(1);
    }

    [Fact]
    public async Task ReleaseHandlers_DifferentTriggersForTheSameEvent_ReleaseOnlyOnce()
    {
        // e.g. cancelled after being rejected by the system: the second release finds nothing active.
        using var host = await HostWithProvisionalBlockAsync();

        await host.IntegrationHandler<EventRequestRejectedIntegrationEvent>().HandleAsync(Rejected());
        await host.IntegrationHandler<HallEventExpiredIntegrationEvent>().HandleAsync(Expired());

        host.Outbox.Count<SlotReleasedIntegrationEvent>().ShouldBe(1);
    }

    [Fact]
    public async Task ReleaseHandlers_NothingWasEverPlaced_DoNotCreateAnEmptyScheduleOrCommit()
    {
        using var host = new HsTestHost();

        await host.IntegrationHandler<EventRequestRejectedIntegrationEvent>().HandleAsync(Rejected());

        host.UnitOfWork.SaveCount.ShouldBe(0);
        host.Schedules.Contains(new HallId(HallGuid), Day).ShouldBeFalse();
    }

    [Fact]
    public async Task ReleaseHandlers_AfterReleasing_TheSlotCanBeBookedByAnotherEvent()
    {
        using var host = await HostWithProvisionalBlockAsync();
        await host.IntegrationHandler<EventRequestRejectedIntegrationEvent>().HandleAsync(Rejected());

        var result = await host.PlaceProvisionalAsync(Guid.NewGuid(), 10, 14);

        result.Success.ShouldBeTrue();
    }
}
