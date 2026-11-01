using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Nodo.BuildingBlocks.Application;
using Nodo.EventLifecycle.Contracts;
using Nodo.HallSchedule.Application;
using Nodo.HallSchedule.Contracts;
using Nodo.HallSchedule.Domain;
using Nodo.TestDoubles;
using static Nodo.HallSchedule.UnitTests.UseCases.HsTestHost;

namespace Nodo.HallSchedule.UnitTests.UseCases;

public class QueryAndOutboxTests
{
    private static readonly Guid EventId = Guid.Parse("44444444-0000-0000-0000-000000000003");
    private static readonly Guid CustomerId = Guid.Parse("22222222-0000-0000-0000-000000000001");

    // ---- Outbox: every integration event of the module is added (same transaction as the aggregate) ----

    [Fact]
    public async Task Outbox_PlacingProvisionalBlock_AddsSlotProvisionallyBlockedIntegrationEvent()
    {
        using var host = new HsTestHost();

        await host.PlaceProvisionalAsync(EventId, 10, 14);

        var published = host.Outbox.Single<SlotProvisionallyBlockedIntegrationEvent>();
        published.EventId.ShouldBe(EventId);
        published.HallId.ShouldBe(HallGuid);
        published.Date.ShouldBe(Day);
        published.Start.ShouldBe(new TimeOnly(10, 0));
        published.End.ShouldBe(new TimeOnly(14, 0));
        published.Id.ShouldNotBe(Guid.Empty);
        published.OccurredOnUtc.ShouldBe(host.Clock.UtcNow);
    }

    [Fact]
    public async Task Outbox_PlacingDefinitiveBlock_AddsSlotBlockConsolidatedIntegrationEvent()
    {
        using var host = new HsTestHost();

        await host.Api.PlaceDefinitiveBlockAsync(EventId, HallGuid, Day, new TimeOnly(10, 0), new TimeOnly(14, 0), []);

        host.Outbox.Single<SlotBlockConsolidatedIntegrationEvent>().EventId.ShouldBe(EventId);
    }

    [Fact]
    public async Task Outbox_ConsolidatingAProvisionalBlock_AddsSlotBlockConsolidatedIntegrationEvent()
    {
        using var host = new HsTestHost();
        await host.PlaceProvisionalAsync(EventId, 10, 14);
        host.Outbox.Clear();

        await host.IntegrationHandler<HallEventFirmlyConfirmedIntegrationEvent>().HandleAsync(
            new HallEventFirmlyConfirmedIntegrationEvent(
                Guid.NewGuid(), host.Clock.UtcNow, EventId, HallGuid, Day, new TimeOnly(10, 0), new TimeOnly(14, 0),
                40, CustomerId, 2000m, 600m, []));

        host.Outbox.Single<SlotBlockConsolidatedIntegrationEvent>().EventId.ShouldBe(EventId);
    }

    [Fact]
    public async Task Outbox_ReleasingABlock_AddsSlotReleasedIntegrationEvent()
    {
        using var host = new HsTestHost();
        await host.PlaceProvisionalAsync(EventId, 10, 14);
        host.Outbox.Clear();

        await host.IntegrationHandler<EventRequestRejectedIntegrationEvent>().HandleAsync(
            new EventRequestRejectedIntegrationEvent(
                Guid.NewGuid(), host.Clock.UtcNow, EventId, HallGuid, Day, "Lucía Paz", "Motivo suficientemente largo"));

        var published = host.Outbox.Single<SlotReleasedIntegrationEvent>();
        published.EventId.ShouldBe(EventId);
        published.Start.ShouldBe(new TimeOnly(10, 0));
    }

    [Fact]
    public async Task Outbox_PlaceConsolidateAndRelease_PublishEveryIntegrationEventOfTheModule()
    {
        using var host = new HsTestHost();
        await host.PlaceProvisionalAsync(EventId, 10, 14);
        await host.IntegrationHandler<HallEventFirmlyConfirmedIntegrationEvent>().HandleAsync(
            new HallEventFirmlyConfirmedIntegrationEvent(
                Guid.NewGuid(), host.Clock.UtcNow, EventId, HallGuid, Day, new TimeOnly(10, 0), new TimeOnly(14, 0),
                40, CustomerId, 2000m, 600m, []));
        await host.IntegrationHandler<HallEventCancelledWithCreditIntegrationEvent>().HandleAsync(
            new HallEventCancelledWithCreditIntegrationEvent(
                Guid.NewGuid(), host.Clock.UtcNow, EventId, HallGuid, Day, CustomerId, 600m));

        var published = host.Outbox.Events.Select(e => e.GetType()).ToHashSet();
        var contracts = typeof(SlotReleasedIntegrationEvent).Assembly.GetTypes()
            .Where(t => t.IsAssignableTo(typeof(IIntegrationEvent)) && t is { IsClass: true, IsAbstract: false });

        contracts.Where(t => !published.Contains(t)).Select(t => t.Name).ShouldBeEmpty();
    }

    [Fact]
    public void Outbox_EveryDomainEventOfTheModule_HasAnApplicationHandlerThatMapsIt()
    {
        using var host = new HsTestHost();

        var domainEvents = TestContainer.DomainEventTypes(typeof(Nodo.HallSchedule.Domain.AssemblyMarker).Assembly).ToList();

        domainEvents.Count.ShouldBe(3);
        foreach (var domainEvent in domainEvents)
            TestContainer.HandlerCount(host.Provider, domainEvent).ShouldBeGreaterThan(0, domainEvent.Name);
    }

    // ---- GetHallCalendar ----

    [Fact]
    public async Task GetHallCalendar_ActiveBlocks_AddBuffersAndCustomerFromTheEventLifecycleApi()
    {
        using var host = new HsTestHost();
        host.CalendarQueries.GetActiveBlocksAsync(HallGuid, Day, Day.AddDays(7), Arg.Any<CancellationToken>())
            .Returns([
                new ScheduleBlockReadModel(Guid.NewGuid(), EventId, Day, new TimeOnly(10, 0), new TimeOnly(14, 0),
                    "Definitive", [new EquipmentRequest("PROJECTOR", 1)])
            ]);
        host.EventLifecycle.GetEventSummariesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new EventSummary(EventId, CustomerId, "Ana Gutiérrez", "Confirmed")]);

        var calendar = await host.Mediator.Send(new GetHallCalendarQuery(HallGuid, Day, Day.AddDays(7)));

        var block = calendar.ShouldHaveSingleItem();
        block.BufferStart.ShouldBe(new TimeOnly(9, 0));   // 1 h setup (BR-03)
        block.BufferEnd.ShouldBe(new TimeOnly(15, 0));    // 1 h cleanup
        block.Kind.ShouldBe("Definitive");
        block.CustomerName.ShouldBe("Ana Gutiérrez");
        block.EventStatus.ShouldBe("Confirmed");
        block.Equipment.Single().Code.ShouldBe("PROJECTOR");
    }

    [Fact]
    public async Task GetHallCalendar_EventUnknownToEventLifecycle_StillReturnsTheBlock()
    {
        using var host = new HsTestHost();
        host.CalendarQueries.GetActiveBlocksAsync(HallGuid, Day, Day, Arg.Any<CancellationToken>())
            .Returns([new ScheduleBlockReadModel(Guid.NewGuid(), EventId, Day, new TimeOnly(10, 0), new TimeOnly(12, 0), "Provisional", [])]);
        host.EventLifecycle.GetEventSummariesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var calendar = await host.Mediator.Send(new GetHallCalendarQuery(HallGuid, Day, Day));

        calendar.Single().CustomerName.ShouldBeNull();
    }

    [Fact]
    public async Task GetHallCalendar_BlocksAreOrderedByDateAndStart_AndLateBufferIsClampedToTheEndOfDay()
    {
        using var host = new HsTestHost();
        var late = Guid.NewGuid();
        var early = Guid.NewGuid();
        host.CalendarQueries.GetActiveBlocksAsync(HallGuid, Day, Day, Arg.Any<CancellationToken>())
            .Returns([
                new ScheduleBlockReadModel(Guid.NewGuid(), late, Day, new TimeOnly(21, 0), new TimeOnly(23, 0), "Provisional", []),
                new ScheduleBlockReadModel(Guid.NewGuid(), early, Day, new TimeOnly(8, 0), new TimeOnly(10, 0), "Provisional", [])
            ]);
        host.EventLifecycle.GetEventSummariesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);

        var calendar = await host.Mediator.Send(new GetHallCalendarQuery(HallGuid, Day, Day));

        calendar.Select(b => b.EventId).ShouldBe([early, late]);
        calendar.Last().BufferEnd.ShouldBe(new TimeOnly(23, 59)); // 24:00 does not exist as a TimeOnly
    }

    [Fact]
    public async Task GetHallCalendar_NoBlocks_ReturnsEmptyWithoutCallingEventLifecycle()
    {
        using var host = new HsTestHost();
        host.CalendarQueries.GetActiveBlocksAsync(HallGuid, Day, Day, Arg.Any<CancellationToken>()).Returns([]);

        var calendar = await host.Mediator.Send(new GetHallCalendarQuery(HallGuid, Day, Day));

        calendar.ShouldBeEmpty();
        await host.EventLifecycle.DidNotReceiveWithAnyArgs().GetEventSummariesAsync(default!, default);
    }

    [Fact]
    public async Task GetHallCalendar_RangeEndsBeforeItStarts_FailsShapeValidation()
    {
        using var host = new HsTestHost();

        await Should.ThrowAsync<ValidationException>(
            () => host.Mediator.Send(new GetHallCalendarQuery(HallGuid, Day.AddDays(2), Day)));
    }

    [Fact]
    public async Task GetHallCalendar_EmptyHallId_FailsShapeValidation()
    {
        using var host = new HsTestHost();

        await Should.ThrowAsync<ValidationException>(
            () => host.Mediator.Send(new GetHallCalendarQuery(Guid.Empty, Day, Day)));
    }

    // ---- CheckAvailability (UI) ----

    [Fact]
    public async Task CheckAvailabilityQuery_TakenSlot_ReturnsUnavailableWithCodeAndReason()
    {
        using var host = new HsTestHost();
        await host.PlaceProvisionalAsync(EventId, 10, 14);

        var result = await host.Mediator.Send(new CheckAvailabilityQuery(
            HallGuid, Day, new TimeOnly(12, 0), new TimeOnly(16, 0), []));

        result.IsAvailable.ShouldBeFalse();
        result.Code.ShouldBe("SCHEDULE.SLOT_OVERLAP");
        result.Reason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CheckAvailabilityQuery_FreeSlot_ReturnsAvailable()
    {
        using var host = new HsTestHost();

        var result = await host.Mediator.Send(new CheckAvailabilityQuery(
            HallGuid, Day, new TimeOnly(10, 0), new TimeOnly(14, 0), []));

        result.IsAvailable.ShouldBeTrue();
    }
}
