using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Nodo.BuildingBlocks.Application;
using Nodo.EventLifecycle.Application;
using Nodo.EventLifecycle.Contracts;
using Nodo.TestDoubles;
using static Nodo.EventLifecycle.UnitTests.UseCases.ElTestHost;

namespace Nodo.EventLifecycle.UnitTests.UseCases;

public class QueriesAndOutboxTests
{
    private static HallEventListItemDto Item(Guid? customer = null, int startHour = 9, DateTime? decisionDeadline = null) => new(
        Guid.NewGuid(), customer ?? TechSol, null, EventDay, new TimeOnly(startHour, 0), new TimeOnly(startHour + 4, 0),
        40, 2000m, "Requested", decisionDeadline ?? new DateTime(2026, 11, 2, 4, 0, 0, DateTimeKind.Utc), null, 0m, 0m);

    // ---- GetHallEvent ----

    [Fact]
    public async Task GetHallEvent_AddsTheCustomerNameToTheStoredData()
    {
        using var host = new ElTestHost();
        var id = Guid.NewGuid();
        var stored = new HallEventDto(
            id, Guid.NewGuid(), TechSol, null, Hall, EventDay, new TimeOnly(9, 0), new TimeOnly(13, 0), 40, 2000m,
            "Confirmed", new DepositDto(30, 600m, 600m, 0m, true, ["TX-1"]), new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc), null,
            new ManagementDecisionDto(Management, new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc), true, null), 0m, 0, true, null,
            [new ExtraServiceDto("OwnEquipment", "PROJECTOR", "Proyector", 1, 150m, 150m)],
            [new HistoryEntryDto("Requested", new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc), "Cliente", "Solicitud creada.")]);
        host.Queries.GetAsync(id, Arg.Any<CancellationToken>()).Returns(stored);

        var result = await host.Mediator.Send(new GetHallEventQuery(id));

        result.CustomerName.ShouldBe("TechSol S.R.L.");
        result.Deposit!.IsCovered.ShouldBeTrue();
        result.History.Single().Actor.ShouldBe("Cliente");
        result.ExtraServices.Single().Code.ShouldBe("PROJECTOR");
    }

    [Fact]
    public async Task GetHallEvent_Unknown_ThrowsNotFound()
    {
        using var host = new ElTestHost();
        host.Queries.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((HallEventDto?)null);

        await Should.ThrowAsync<NotFoundException>(() => host.Mediator.Send(new GetHallEventQuery(Guid.NewGuid())));
    }

    // ---- ListHallEvents ----

    [Fact]
    public async Task ListHallEvents_PassesTheFiltersAndNamesEveryCustomer()
    {
        using var host = new ElTestHost();
        var from = new DateOnly(2026, 11, 1);
        var to = new DateOnly(2026, 11, 30);
        host.Queries.ListAsync("Requested", TechSol, from, to, Arg.Any<CancellationToken>())
            .Returns([Item(), Item(Guid.NewGuid())]);

        var list = await host.Mediator.Send(new ListHallEventsQuery("Requested", TechSol, from, to));

        list.Count.ShouldBe(2);
        list.First().CustomerName.ShouldBe("TechSol S.R.L.");
        list.Last().CustomerName.ShouldBeNull();
    }

    [Fact]
    public async Task ListHallEvents_UnknownStatus_FailsShapeValidation()
    {
        using var host = new ElTestHost();

        await Should.ThrowAsync<ValidationException>(() => host.Mediator.Send(new ListHallEventsQuery("Pendiente")));
    }

    [Fact]
    public async Task ListHallEvents_RangeEndsBeforeItStarts_FailsShapeValidation()
    {
        using var host = new ElTestHost();

        await Should.ThrowAsync<ValidationException>(() => host.Mediator.Send(
            new ListHallEventsQuery(From: new DateOnly(2026, 11, 10), To: new DateOnly(2026, 11, 1))));
    }

    // ---- GetPendingApprovals ----

    [Fact]
    public async Task GetPendingApprovals_MostUrgentFirst()
    {
        using var host = new ElTestHost();
        var later = Item(decisionDeadline: new DateTime(2026, 11, 3, 4, 0, 0, DateTimeKind.Utc));
        var sooner = Item(decisionDeadline: new DateTime(2026, 11, 2, 4, 0, 0, DateTimeKind.Utc));
        host.Queries.GetPendingApprovalsAsync(Arg.Any<CancellationToken>()).Returns([later, sooner]);

        var inbox = await host.Mediator.Send(new GetPendingApprovalsQuery());

        inbox.Select(i => i.Id).ShouldBe([sooner.Id, later.Id]);
    }

    // ---- GetTodayOperations ----

    [Fact]
    public async Task GetTodayOperations_UsesTheLocalDateOfTheClock_NotTheUtcDate()
    {
        using var host = new ElTestHost();
        host.Clock.UtcNow = new DateTime(2026, 11, 12, 2, 0, 0, DateTimeKind.Utc); // 22:00 on 11 Nov in La Paz
        host.Queries.GetTodayOperationsAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([Item(startHour: 14), Item(startHour: 9)]);

        var today = await host.Mediator.Send(new GetTodayOperationsQuery());

        await host.Queries.Received(1).GetTodayOperationsAsync(new DateOnly(2026, 11, 11), Arg.Any<CancellationToken>());
        today.Select(i => i.Start.Hour).ShouldBe([9, 14]);
    }

    // ---- GetCustomerCredit ----

    [Fact]
    public async Task GetCustomerCredit_WithCredit_ReturnsBalanceMovementsAndName()
    {
        using var host = new ElTestHost();
        host.Queries.GetCreditAsync(TechSol, Arg.Any<CancellationToken>()).Returns(new CustomerCreditDto(
            TechSol, null, 600m, [new CreditMovementDto("Granted", 600m, Guid.NewGuid(), "Cancelación del evento", new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc))]));

        var credit = await host.Mediator.Send(new GetCustomerCreditQuery(TechSol));

        credit.Balance.ShouldBe(600m);
        credit.CustomerName.ShouldBe("TechSol S.R.L.");
        credit.Movements.Single().Reason.ShouldBe("Cancelación del evento");
    }

    [Fact]
    public async Task GetCustomerCredit_CustomerNeverHadCredit_ReturnsAZeroBalanceInsteadOfNotFound()
    {
        using var host = new ElTestHost();
        host.Queries.GetCreditAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((CustomerCreditDto?)null);

        var credit = await host.Mediator.Send(new GetCustomerCreditQuery(TechSol));

        credit.Balance.ShouldBe(0m);
        credit.Movements.ShouldBeEmpty();
    }

    // ---- Module API consumed by the hall calendar ----

    [Fact]
    public async Task ModuleApi_GetEventSummaries_AddsCustomerNames()
    {
        using var host = new ElTestHost();
        var eventId = Guid.NewGuid();
        host.Queries.GetSummariesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new EventSummary(eventId, TechSol, null, "Confirmed")]);
        using var scope = host.Provider.CreateScope();

        var summaries = await scope.ServiceProvider.GetRequiredService<IEventLifecycleModuleApi>()
            .GetEventSummariesAsync([eventId]);

        summaries.Single().CustomerName.ShouldBe("TechSol S.R.L.");
        summaries.Single().Status.ShouldBe("Confirmed");
    }

    // ---- Outbox: coverage of the module's integration events ----

    [Fact]
    public void Outbox_EveryDomainEventOfTheModule_HasAnApplicationHandlerThatMapsIt()
    {
        using var host = new ElTestHost();

        var domainEvents = TestContainer.DomainEventTypes(typeof(Nodo.EventLifecycle.Domain.AssemblyMarker).Assembly).ToList();

        domainEvents.Count.ShouldBe(13);
        foreach (var domainEvent in domainEvents)
            TestContainer.HandlerCount(host.Provider, domainEvent).ShouldBeGreaterThan(0, domainEvent.Name);
    }

    [Fact]
    public async Task Outbox_AFullLifecycle_PublishesEveryIntegrationEventOfTheModule()
    {
        using var host = new ElTestHost();

        // Request -> approve -> pay with excess -> confirm -> start -> complete, then a second event that is cancelled
        // and rescheduled, and a third one that is rejected and a fourth one that expires.
        var main = await host.ApprovedEventAsync(message: QuoteAcceptedMessage(extras: [Projector()]));
        await host.Mediator.Send(new AccreditDepositCommand(main.Id.Value, 700m, "BOB", "TX-1"));
        host.Clock.UtcNow = main.Slot.StartUtc;
        await host.Mediator.Send(new StartHallEventCommand(main.Id.Value, Operations));
        await host.Mediator.Send(new CompleteHallEventCommand(main.Id.Value, Operations));

        var rescheduled = await host.CancelledEventWithCreditAsync(QuoteAcceptedMessage(date: EventDay.AddDays(5)));
        host.Clock.UtcNow = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        await host.Mediator.Send(new RescheduleHallEventCommand(
            rescheduled.Id.Value, EventDay.AddDays(15), new TimeOnly(10, 0), Reception));

        var rejected = await host.RequestedEventAsync(QuoteAcceptedMessage(date: EventDay.AddDays(7)));
        await host.Mediator.Send(new RejectEventRequestCommand(rejected.Id.Value, "No hay personal ese día.", Management));

        await host.RequestedEventAsync(QuoteAcceptedMessage(date: EventDay.AddDays(9)));
        host.Clock.Advance(TimeSpan.FromHours(20));
        await host.Mediator.Send(new ExpireOverdueHallEventsCommand());

        var published = host.Outbox.Events.Select(e => e.GetType()).ToHashSet();
        var contracts = typeof(EventRequestCreatedIntegrationEvent).Assembly.GetTypes()
            .Where(t => t.IsAssignableTo(typeof(IIntegrationEvent)) && t is { IsClass: true, IsAbstract: false });

        contracts.Where(t => !published.Contains(t)).Select(t => t.Name).ShouldBeEmpty();
    }
}
