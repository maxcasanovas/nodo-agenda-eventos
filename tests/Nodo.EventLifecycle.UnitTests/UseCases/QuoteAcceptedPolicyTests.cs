using NSubstitute;
using Nodo.EventLifecycle.Contracts;
using Nodo.EventLifecycle.Domain;
using Nodo.HallSchedule.Contracts;
using Nodo.Quotes.Contracts;
using Nodo.SharedKernel;
using static Nodo.EventLifecycle.UnitTests.UseCases.ElTestHost;

namespace Nodo.EventLifecycle.UnitTests.UseCases;

/// <summary>BR-11: the policy that turns an accepted quote into an event request and blocks the hall.</summary>
public class QuoteAcceptedPolicyTests
{
    [Fact]
    public async Task Handle_AcceptedQuote_CreatesRequestedEventWithTheFrozenSnapshot()
    {
        using var host = new ElTestHost();

        var hallEvent = await host.RequestedEventAsync(
            QuoteAcceptedMessage(attendance: 35, total: 2800m, extras: [Projector(), CoffeeBreak(35)]));

        hallEvent.Status.ShouldBe(HallEventStatus.Requested);
        hallEvent.CustomerId.Value.ShouldBe(TechSol);
        hallEvent.HallId.Value.ShouldBe(Hall);
        hallEvent.Slot.Date.ShouldBe(EventDay);
        hallEvent.Slot.DurationHours.ShouldBe(4);
        hallEvent.Attendance.Value.ShouldBe(35);
        hallEvent.Total.Amount.ShouldBe(2800m);
        hallEvent.ExtraServices.Select(e => (e.Code, e.Type, e.Quantity, e.UnitCost.Amount)).ShouldBe(
            [("PROJECTOR", ExtraServiceType.OwnEquipment, 1, 150m), ("COFFEE_BREAK", ExtraServiceType.ExternalCatering, 35, 35m)],
            ignoreOrder: true);
        hallEvent.CreditGenerated.Amount.ShouldBe(0m);
    }

    [Fact]
    public async Task Handle_AcceptedQuote_PlacesAProvisionalBlockWithOnlyTheOwnEquipment()
    {
        using var host = new ElTestHost();

        var hallEvent = await host.RequestedEventAsync(QuoteAcceptedMessage(extras: [Projector(), CoffeeBreak()]));

        await host.HallSchedule.Received(1).PlaceProvisionalBlockAsync(
            hallEvent.Id.Value, Hall, EventDay, new TimeOnly(9, 0), new TimeOnly(13, 0),
            Arg.Is<IReadOnlyCollection<EquipmentRequest>>(e => e.Single().Code == "PROJECTOR" && e.Single().Quantity == 1),
            Arg.Any<CancellationToken>());
        await host.HallSchedule.DidNotReceiveWithAnyArgs()
            .PlaceDefinitiveBlockAsync(default, default, default, default, default, default!, default);
    }

    [Fact]
    public async Task Handle_AcceptedQuote_AddsEventRequestCreatedToTheOutboxInTheSameCommit()
    {
        using var host = new ElTestHost();
        var message = QuoteAcceptedMessage();

        var hallEvent = await host.RequestedEventAsync(message);

        var published = host.Outbox.Single<EventRequestCreatedIntegrationEvent>();
        published.EventId.ShouldBe(hallEvent.Id.Value);
        published.HallId.ShouldBe(Hall);
        published.Date.ShouldBe(EventDay);
        published.Start.ShouldBe(new TimeOnly(9, 0));
        published.End.ShouldBe(new TimeOnly(13, 0));
        published.Attendance.ShouldBe(40);
        published.CustomerId.ShouldBe(TechSol);
        published.Id.ShouldNotBe(Guid.Empty);
        host.UnitOfWork.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_SameMessageTwice_CreatesASingleEvent()
    {
        using var host = new ElTestHost();
        var message = QuoteAcceptedMessage();

        await host.QuoteAcceptedHandler.HandleAsync(message);
        await host.QuoteAcceptedHandler.HandleAsync(message); // at-least-once delivery

        host.Events.All.Count.ShouldBe(1);
        host.Outbox.Count<EventRequestCreatedIntegrationEvent>().ShouldBe(1);
        await host.HallSchedule.Received(1).PlaceProvisionalBlockAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<TimeOnly>(), Arg.Any<TimeOnly>(),
            Arg.Any<IReadOnlyCollection<EquipmentRequest>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DifferentMessages_CreateIndependentEvents()
    {
        using var host = new ElTestHost();

        await host.QuoteAcceptedHandler.HandleAsync(QuoteAcceptedMessage(date: EventDay));
        await host.QuoteAcceptedHandler.HandleAsync(QuoteAcceptedMessage(date: EventDay.AddDays(1)));

        host.Events.All.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Handle_HallNoLongerFree_CreatesTheRequestAndRejectsItWithTheSystemReason()
    {
        using var host = new ElTestHost();
        host.HallScheduleRejectsReservations();

        var hallEvent = await host.RequestedEventAsync();

        hallEvent.Status.ShouldBe(HallEventStatus.Rejected);
        hallEvent.Decision!.DecidedBy.ShouldBe("Sistema");
        hallEvent.Decision.RejectionReason.ShouldBe("Sin disponibilidad al registrar la solicitud");
        hallEvent.History.Select(h => h.Status).ShouldBe([HallEventStatus.Requested, HallEventStatus.Rejected]);
        host.UnitOfWork.SaveCount.ShouldBe(2); // created first, rejected afterwards
    }

    [Fact]
    public async Task Handle_HallNoLongerFree_PublishesCreatedAndRejectedSoTheScheduleCanReleaseAnyLeftover()
    {
        using var host = new ElTestHost();
        host.HallScheduleRejectsReservations();

        var hallEvent = await host.RequestedEventAsync();

        host.Outbox.Single<EventRequestCreatedIntegrationEvent>().EventId.ShouldBe(hallEvent.Id.Value);
        var rejected = host.Outbox.Single<EventRequestRejectedIntegrationEvent>();
        rejected.EventId.ShouldBe(hallEvent.Id.Value);
        rejected.HallId.ShouldBe(Hall);
        rejected.Date.ShouldBe(EventDay);
        rejected.RejectedBy.ShouldBe("Sistema");
        rejected.Reason.ShouldBe("Sin disponibilidad al registrar la solicitud");
    }

    [Fact]
    public async Task Handle_EquipmentAboveStock_ThrowsExtraServiceInvalidAndSavesNothing()
    {
        using var host = new ElTestHost();
        var tooMany = new QuoteExtraServiceDto("OwnEquipment", "PROJECTOR", "Proyector", 2, 150m);

        var ex = await Should.ThrowAsync<DomainException>(
            () => host.QuoteAcceptedHandler.HandleAsync(QuoteAcceptedMessage(extras: tooMany)));

        ex.Code.ShouldBe("EVENT.EXTRA_SERVICE_INVALID");
        host.Events.All.ShouldBeEmpty();
        host.UnitOfWork.SaveCount.ShouldBe(0);
        host.Outbox.Events.ShouldBeEmpty();
        await host.HallSchedule.DidNotReceiveWithAnyArgs()
            .PlaceProvisionalBlockAsync(default, default, default, default, default, default!, default);
    }

    [Fact]
    public async Task Handle_EventDateInsideTheMinimumNotice_ThrowsDateOutOfRange()
    {
        using var host = new ElTestHost();

        var ex = await Should.ThrowAsync<DomainException>(() => host.QuoteAcceptedHandler.HandleAsync(
            QuoteAcceptedMessage(date: new DateOnly(2026, 11, 3)))); // today + 2

        ex.Code.ShouldBe("QUOTE.DATE_OUT_OF_RANGE");
        host.Events.All.ShouldBeEmpty();
    }
}
