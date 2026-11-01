using FluentValidation;
using NSubstitute;
using Nodo.BuildingBlocks.Application;
using Nodo.HallSchedule.Contracts;
using Nodo.Quotes.Application;
using Nodo.Quotes.Contracts;
using Nodo.Quotes.Domain;
using Nodo.SharedKernel;
using Nodo.TestDoubles;
using static Nodo.Quotes.UnitTests.UseCases.FakeMembershipDirectory;

namespace Nodo.Quotes.UnitTests.UseCases;

public class AcceptExpireAndQueryTests
{
    private const string Reception = "Recepción Nodo";

    // ---- AcceptQuote ----

    [Fact]
    public async Task AcceptQuote_AvailableSlot_AcceptsAndPublishesTheFullSnapshot()
    {
        using var host = new QtTestHost();
        var issued = await host.IssueAsync(host.IssueCommand(
            customerId: TechSol, startHour: 9, endHour: 15, attendance: 70,
            extras: [new ExtraServiceRequest("PROJECTOR"), new ExtraServiceRequest("LUNCH", 70)]));
        host.Outbox.Clear();
        host.Clock.Advance(TimeSpan.FromHours(2));

        var accepted = await host.Mediator.Send(new AcceptQuoteCommand(issued.QuoteId, Reception));

        accepted.Status.ShouldBe("Accepted");
        host.Quotes.All.Single().Status.ShouldBe(QuoteStatus.Accepted);
        var snapshot = host.Outbox.Single<QuoteAcceptedIntegrationEvent>();
        snapshot.QuoteId.ShouldBe(issued.QuoteId);
        snapshot.CustomerId.ShouldBe(TechSol);
        snapshot.HallId.ShouldBe(QtTestHost.Hall);
        snapshot.Date.ShouldBe(QtTestHost.EventDay);
        snapshot.Start.ShouldBe(new TimeOnly(9, 0));
        snapshot.End.ShouldBe(new TimeOnly(15, 0));
        snapshot.Attendance.ShouldBe(70);
        snapshot.BaseHourlyRate.ShouldBe(500m);
        snapshot.Hours.ShouldBe(6);
        snapshot.RateFactor.ShouldBe(0.8m);
        snapshot.HallRental.ShouldBe(2400m);
        snapshot.MembershipDiscountPercentage.ShouldBe(10);
        snapshot.MembershipDiscount.ShouldBe(240m);
        snapshot.ExtrasTotal.ShouldBe(150m + 6300m);
        snapshot.Total.ShouldBe(issued.Price.Total);
        snapshot.ExtraServices.Select(e => (e.Type, e.Code, e.Quantity, e.UnitCost)).ShouldBe(
            [("OwnEquipment", "PROJECTOR", 1, 150m), ("ExternalCatering", "LUNCH", 70, 90m)]);
    }

    [Fact]
    public async Task AcceptQuote_ChecksAvailabilityOfTheQuotedSlotAndEquipment_Br10()
    {
        using var host = new QtTestHost();
        var issued = await host.IssueAsync(host.IssueCommand(extras: [new ExtraServiceRequest("TV", 2)]));
        host.HallSchedule.ClearReceivedCalls();

        await host.Mediator.Send(new AcceptQuoteCommand(issued.QuoteId, Reception));

        await host.HallSchedule.Received(1).CheckAvailabilityAsync(
            QtTestHost.Hall, QtTestHost.EventDay, new TimeOnly(9, 0), new TimeOnly(13, 0),
            Arg.Is<IReadOnlyCollection<EquipmentRequest>>(e => e.Single().Code == "TV" && e.Single().Quantity == 2),
            null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcceptQuote_NoAvailability_FailsWithTheScheduleCodeAndSavesNothing_Br10()
    {
        using var host = new QtTestHost();
        var issued = await host.IssueAsync(); // availability is only informative here
        host.Outbox.Clear();
        var commits = host.UnitOfWork.SaveCount;
        host.HallScheduleIsBusy("SCHEDULE.SLOT_OVERLAP", "Otro evento ocupa esa franja.");

        var ex = await Should.ThrowAsync<DomainException>(
            () => host.Mediator.Send(new AcceptQuoteCommand(issued.QuoteId, Reception)));

        ex.Code.ShouldBe("SCHEDULE.SLOT_OVERLAP");
        ex.Message.ShouldBe("Otro evento ocupa esa franja.");
        host.UnitOfWork.SaveCount.ShouldBe(commits);
        host.Outbox.Events.ShouldBeEmpty(); // no QuoteAccepted leaves the module
    }

    [Fact]
    public async Task AcceptQuote_ExpiredQuote_FailsWithQuoteExpiredBeforeAskingTheSchedule()
    {
        using var host = new QtTestHost();
        var issued = await host.IssueAsync();
        host.HallSchedule.ClearReceivedCalls();
        host.Clock.Advance(TimeSpan.FromHours(96));

        var ex = await Should.ThrowAsync<DomainException>(
            () => host.Mediator.Send(new AcceptQuoteCommand(issued.QuoteId, Reception)));

        ex.Code.ShouldBe("QUOTE.EXPIRED");
        await host.HallSchedule.DidNotReceiveWithAnyArgs()
            .CheckAvailabilityAsync(default, default, default, default, default!, default, default);
    }

    [Fact]
    public async Task AcceptQuote_JustBeforeTheyExpire_Succeeds()
    {
        using var host = new QtTestHost();
        var issued = await host.IssueAsync(host.IssueCommand(date: QtTestHost.EventDay.AddDays(10)));
        host.Clock.Advance(TimeSpan.FromHours(95).Add(TimeSpan.FromMinutes(59)));

        var accepted = await host.Mediator.Send(new AcceptQuoteCommand(issued.QuoteId, Reception));

        accepted.Status.ShouldBe("Accepted");
    }

    [Fact]
    public async Task AcceptQuote_EventDateNoLongerInsideTheBookingWindow_Fails()
    {
        using var host = new QtTestHost();
        var issued = await host.IssueAsync(host.IssueCommand(date: new DateOnly(2026, 11, 5))); // today + 4
        host.Clock.Advance(TimeSpan.FromHours(48)); // today is now 3 Nov: the event is 2 days away

        var ex = await Should.ThrowAsync<DomainException>(
            () => host.Mediator.Send(new AcceptQuoteCommand(issued.QuoteId, Reception)));

        ex.Code.ShouldBe("QUOTE.DATE_OUT_OF_RANGE");
    }

    [Fact]
    public async Task AcceptQuote_AlreadyAccepted_Fails()
    {
        using var host = new QtTestHost();
        var issued = await host.IssueAsync();
        await host.Mediator.Send(new AcceptQuoteCommand(issued.QuoteId, Reception));

        var ex = await Should.ThrowAsync<DomainException>(
            () => host.Mediator.Send(new AcceptQuoteCommand(issued.QuoteId, Reception)));

        ex.Code.ShouldBe("QUOTE.INVALID_STATE_TRANSITION");
        host.Outbox.Count<QuoteAcceptedIntegrationEvent>().ShouldBe(1);
    }

    [Fact]
    public async Task AcceptQuote_UnknownQuote_ThrowsNotFound()
    {
        using var host = new QtTestHost();

        await Should.ThrowAsync<NotFoundException>(
            () => host.Mediator.Send(new AcceptQuoteCommand(Guid.NewGuid(), Reception)));
    }

    [Fact]
    public async Task AcceptQuote_EmptyIdOrPerformer_FailsShapeValidation()
    {
        using var host = new QtTestHost();

        await Should.ThrowAsync<ValidationException>(() => host.Mediator.Send(new AcceptQuoteCommand(Guid.Empty, Reception)));
        await Should.ThrowAsync<ValidationException>(() => host.Mediator.Send(new AcceptQuoteCommand(Guid.NewGuid(), "")));
    }

    // ---- ExpireDueQuotes ----

    [Fact]
    public async Task ExpireDueQuotes_OnlyOverdueIssuedQuotesExpire_AndEachPublishesQuoteExpired()
    {
        using var host = new QtTestHost();
        var old = await host.IssueAsync(host.IssueCommand(date: QtTestHost.EventDay.AddDays(20)));
        host.Clock.Advance(TimeSpan.FromHours(50));
        var recent = await host.IssueAsync(host.IssueCommand(date: QtTestHost.EventDay.AddDays(20)));
        host.Clock.Advance(TimeSpan.FromHours(47)); // old: 97 h, recent: 47 h
        host.Outbox.Clear();

        var expired = await host.Mediator.Send(new ExpireDueQuotesCommand());

        expired.ShouldBe(1);
        host.Quotes.All.Single(q => q.Id.Value == old.QuoteId).Status.ShouldBe(QuoteStatus.Expired);
        host.Quotes.All.Single(q => q.Id.Value == recent.QuoteId).Status.ShouldBe(QuoteStatus.Issued);
        var published = host.Outbox.Single<QuoteExpiredIntegrationEvent>();
        published.QuoteId.ShouldBe(old.QuoteId);
        published.CustomerId.ShouldBe(Ana);
    }

    [Fact]
    public async Task ExpireDueQuotes_AcceptedQuotesAreLeftAlone()
    {
        using var host = new QtTestHost();
        var issued = await host.IssueAsync(host.IssueCommand(date: QtTestHost.EventDay.AddDays(20)));
        await host.Mediator.Send(new AcceptQuoteCommand(issued.QuoteId, Reception));
        host.Clock.Advance(TimeSpan.FromHours(200));

        (await host.Mediator.Send(new ExpireDueQuotesCommand())).ShouldBe(0);

        host.Quotes.All.Single().Status.ShouldBe(QuoteStatus.Accepted);
    }

    [Fact]
    public async Task ExpireDueQuotes_NothingDue_DoesNotCommit_AndRunningTwiceIsHarmless()
    {
        using var host = new QtTestHost();
        await host.IssueAsync();
        var commits = host.UnitOfWork.SaveCount;

        (await host.Mediator.Send(new ExpireDueQuotesCommand())).ShouldBe(0);
        host.UnitOfWork.SaveCount.ShouldBe(commits);

        host.Clock.Advance(TimeSpan.FromHours(100));
        (await host.Mediator.Send(new ExpireDueQuotesCommand())).ShouldBe(1);
        (await host.Mediator.Send(new ExpireDueQuotesCommand())).ShouldBe(0);
    }

    // ---- GetQuote / ListQuotes ----

    private static QuoteDto StoredQuote(Guid id, Guid customerId, string status, DateTime expiresAtUtc) => new(
        id, customerId, null, QtTestHost.Hall, QtTestHost.EventDay, new TimeOnly(9, 0), new TimeOnly(13, 0), 40, status,
        expiresAtUtc.AddHours(-96), expiresAtUtc, null, 0,
        new PriceBreakdownDto(500m, 4, 0.9m, 1800m, 0, 0m, 0m, 1800m), []);

    [Fact]
    public async Task GetQuote_Issued_AddsCustomerNameAndHoursRemaining()
    {
        using var host = new QtTestHost();
        var id = Guid.NewGuid();
        var expires = host.Clock.UtcNow.AddHours(30);
        host.Queries.GetAsync(id, Arg.Any<CancellationToken>()).Returns(StoredQuote(id, Ana, "Issued", expires));

        var quote = await host.Mediator.Send(new GetQuoteQuery(id));

        quote.CustomerName.ShouldBe("Ana Gutiérrez");
        quote.HoursRemaining.ShouldBe(30);
        quote.ExpiresAtUtc.ShouldBe(expires);
    }

    [Fact]
    public async Task GetQuote_IssuedButPastItsExpiry_ShowsZeroHoursRemaining()
    {
        using var host = new QtTestHost();
        var id = Guid.NewGuid();
        host.Queries.GetAsync(id, Arg.Any<CancellationToken>())
            .Returns(StoredQuote(id, Ana, "Issued", host.Clock.UtcNow.AddHours(-3)));

        (await host.Mediator.Send(new GetQuoteQuery(id))).HoursRemaining.ShouldBe(0);
    }

    [Theory]
    [InlineData("Accepted")]
    [InlineData("Expired")]
    public async Task GetQuote_NotIssued_HasNoHoursRemaining(string status)
    {
        using var host = new QtTestHost();
        var id = Guid.NewGuid();
        host.Queries.GetAsync(id, Arg.Any<CancellationToken>())
            .Returns(StoredQuote(id, Ana, status, host.Clock.UtcNow.AddHours(30)));

        (await host.Mediator.Send(new GetQuoteQuery(id))).HoursRemaining.ShouldBe(0);
    }

    [Fact]
    public async Task GetQuote_Unknown_ThrowsNotFound()
    {
        using var host = new QtTestHost();
        host.Queries.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((QuoteDto?)null);

        await Should.ThrowAsync<NotFoundException>(() => host.Mediator.Send(new GetQuoteQuery(Guid.NewGuid())));
    }

    [Fact]
    public async Task ListQuotes_PassesFiltersAndEnrichesEveryRow()
    {
        using var host = new QtTestHost();
        var now = host.Clock.UtcNow;
        host.Queries.ListAsync(Ana, "Issued", Arg.Any<CancellationToken>()).Returns([
            StoredQuote(Guid.NewGuid(), Ana, "Issued", now.AddHours(10)),
            StoredQuote(Guid.NewGuid(), Ana, "Issued", now.AddHours(70))
        ]);

        var list = await host.Mediator.Send(new ListQuotesQuery(Ana, "Issued"));

        list.Select(q => q.HoursRemaining).ShouldBe([10d, 70d]);
        list.ShouldAllBe(q => q.CustomerName == "Ana Gutiérrez");
    }

    [Fact]
    public async Task ListQuotes_UnknownStatus_FailsShapeValidation()
    {
        using var host = new QtTestHost();

        await Should.ThrowAsync<ValidationException>(() => host.Mediator.Send(new ListQuotesQuery(null, "Pendiente")));
    }

    // ---- Outbox coverage ----

    [Fact]
    public async Task Outbox_IssueAcceptAndExpire_PublishEveryIntegrationEventOfTheModule()
    {
        using var host = new QtTestHost();
        var accepted = await host.IssueAsync(host.IssueCommand(date: QtTestHost.EventDay.AddDays(20)));
        await host.Mediator.Send(new AcceptQuoteCommand(accepted.QuoteId, Reception));
        await host.IssueAsync(host.IssueCommand(date: QtTestHost.EventDay.AddDays(20)));
        host.Clock.Advance(TimeSpan.FromHours(100));
        await host.Mediator.Send(new ExpireDueQuotesCommand());

        var published = host.Outbox.Events.Select(e => e.GetType()).ToHashSet();
        var contracts = typeof(QuoteIssuedIntegrationEvent).Assembly.GetTypes()
            .Where(t => t.IsAssignableTo(typeof(IIntegrationEvent)) && t is { IsClass: true, IsAbstract: false });

        contracts.Where(t => !published.Contains(t)).Select(t => t.Name).ShouldBeEmpty();
    }

    [Fact]
    public async Task Outbox_EveryDomainEventOfTheModule_HasAnApplicationHandlerThatMapsIt()
    {
        using var host = new QtTestHost();

        var domainEvents = TestContainer.DomainEventTypes(typeof(Nodo.Quotes.Domain.AssemblyMarker).Assembly).ToList();

        domainEvents.Count.ShouldBe(3);
        foreach (var domainEvent in domainEvents)
            TestContainer.HandlerCount(host.Provider, domainEvent).ShouldBeGreaterThan(0, domainEvent.Name);
        await Task.CompletedTask;
    }
}
