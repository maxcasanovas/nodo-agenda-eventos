using Nodo.EventLifecycle.Application;
using Nodo.EventLifecycle.Contracts;
using Nodo.EventLifecycle.Domain;
using Nodo.SharedKernel;
using static Nodo.EventLifecycle.UnitTests.UseCases.ElTestHost;

namespace Nodo.EventLifecycle.UnitTests.UseCases;

public class ExecutionCancelAndExpireTests
{
    // ---- StartHallEvent / CompleteHallEvent ----

    [Fact]
    public async Task Start_WithinTheWindow_StartsAndPublishesHallEventStarted()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.ConfirmedEventAsync();
        host.Clock.UtcNow = hallEvent.Slot.StartUtc.AddMinutes(-30);
        host.ResetObservations();

        var result = await host.Mediator.Send(new StartHallEventCommand(hallEvent.Id.Value, Operations));

        result.Status.ShouldBe("InProgress");
        var published = host.Outbox.Single<HallEventStartedIntegrationEvent>();
        published.EventId.ShouldBe(hallEvent.Id.Value);
        published.StartedBy.ShouldBe(Operations);
    }

    [Fact]
    public async Task Start_MoreThanOneHourBeforeTheStart_ThrowsStartOutsideWindow()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.ConfirmedEventAsync();
        host.Clock.UtcNow = hallEvent.Slot.StartUtc.AddHours(-1).AddSeconds(-1);
        var commits = host.UnitOfWork.SaveCount;

        var ex = await Should.ThrowAsync<DomainException>(
            () => host.Mediator.Send(new StartHallEventCommand(hallEvent.Id.Value, Operations)));

        ex.Code.ShouldBe("EVENT.START_OUTSIDE_WINDOW");
        host.UnitOfWork.SaveCount.ShouldBe(commits);
    }

    [Fact]
    public async Task Complete_InProgressEvent_CompletesAndPublishesTheAmountLeftToSettle()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.ConfirmedEventAsync();
        host.Clock.UtcNow = hallEvent.Slot.StartUtc;
        await host.Mediator.Send(new StartHallEventCommand(hallEvent.Id.Value, Operations));
        host.ResetObservations();

        var result = await host.Mediator.Send(new CompleteHallEventCommand(hallEvent.Id.Value, Operations));

        result.Status.ShouldBe("Completed");
        var published = host.Outbox.Single<HallEventCompletedIntegrationEvent>();
        published.CustomerId.ShouldBe(TechSol);
        published.Total.ShouldBe(2000m);
        published.DepositAccredited.ShouldBe(600m);
        published.RemainingToSettle.ShouldBe(1400m);
    }

    [Fact]
    public async Task Complete_EventNotInProgress_ThrowsInvalidStateTransition()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.ConfirmedEventAsync();

        var ex = await Should.ThrowAsync<DomainException>(
            () => host.Mediator.Send(new CompleteHallEventCommand(hallEvent.Id.Value, Operations)));

        ex.Code.ShouldBe("EVENT.INVALID_STATE_TRANSITION");
    }

    // ---- CancelHallEvent ----

    [Fact]
    public async Task Cancel_ConfirmedEvent_GrantsCustomerCreditEqualToWhatWasAccredited()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.ConfirmedEventAsync();
        host.ResetObservations();

        var result = await host.Mediator.Send(new CancelHallEventCommand(hallEvent.Id.Value, "Cambio de planes", Reception));

        result.Status.ShouldBe("Cancelled");
        host.Credits.Peek(new CustomerId(TechSol))!.Balance.Amount.ShouldBe(600m);
        hallEvent.CreditGenerated.Amount.ShouldBe(600m);
    }

    [Fact]
    public async Task Cancel_ConfirmedEvent_PublishesCancelledAndCreditGrantedInOneCommit()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.ConfirmedEventAsync();
        host.ResetObservations();
        var commits = host.UnitOfWork.SaveCount;

        await host.Mediator.Send(new CancelHallEventCommand(hallEvent.Id.Value, "Cambio de planes", Reception));

        host.UnitOfWork.SaveCount.ShouldBe(commits + 1);
        var cancelled = host.Outbox.Single<HallEventCancelledWithCreditIntegrationEvent>();
        cancelled.EventId.ShouldBe(hallEvent.Id.Value);
        cancelled.HallId.ShouldBe(Hall);
        cancelled.Date.ShouldBe(EventDay);
        cancelled.CustomerId.ShouldBe(TechSol);
        cancelled.CreditAmount.ShouldBe(600m);
        var granted = host.Outbox.Single<CustomerCreditGrantedIntegrationEvent>();
        granted.Amount.ShouldBe(600m);
        granted.Balance.ShouldBe(600m);
    }

    [Fact]
    public async Task Cancel_CustomerAlreadyHasCredit_IncrementsIt()
    {
        using var host = new ElTestHost();
        await host.CancelledEventWithCreditAsync(); // 600
        var second = await host.ConfirmedEventAsync(QuoteAcceptedMessage(date: EventDay.AddDays(3)));

        await host.Mediator.Send(new CancelHallEventCommand(second.Id.Value, null, Reception));

        host.Credits.All.Count.ShouldBe(1);
        host.Credits.Peek(new CustomerId(TechSol))!.Balance.Amount.ShouldBe(1200m);
    }

    [Fact]
    public async Task Cancel_RequestedEventWithoutPayment_CreatesNoCredit()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.RequestedEventAsync();
        host.ResetObservations();

        await host.Mediator.Send(new CancelHallEventCommand(hallEvent.Id.Value, "Duplicado", Reception));

        host.Credits.All.ShouldBeEmpty();
        host.Outbox.Single<HallEventCancelledWithCreditIntegrationEvent>().CreditAmount.ShouldBe(0m);
        host.Outbox.Count<CustomerCreditGrantedIntegrationEvent>().ShouldBe(0);
    }

    [Fact]
    public async Task Cancel_EventInProgress_ThrowsCancellationNotAllowedAndSavesNothing()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.ConfirmedEventAsync();
        host.Clock.UtcNow = hallEvent.Slot.StartUtc;
        await host.Mediator.Send(new StartHallEventCommand(hallEvent.Id.Value, Operations));
        host.ResetObservations();
        var commits = host.UnitOfWork.SaveCount;

        var ex = await Should.ThrowAsync<DomainException>(
            () => host.Mediator.Send(new CancelHallEventCommand(hallEvent.Id.Value, "tarde", Reception)));

        ex.Code.ShouldBe("EVENT.CANCELLATION_NOT_ALLOWED");
        host.UnitOfWork.SaveCount.ShouldBe(commits);
        host.Outbox.Events.ShouldBeEmpty();
        host.Credits.All.ShouldBeEmpty(); // nothing was paid back, nothing was granted
    }

    // ---- ExpireOverdueHallEvents ----

    [Fact]
    public async Task Expire_RequestWithoutDecisionByTheEndOfTheDay_ExpiresAndPublishesHallEventExpired()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.RequestedEventAsync();
        host.Clock.Advance(TimeSpan.FromHours(16)); // past local midnight
        host.ResetObservations();

        var count = await host.Mediator.Send(new ExpireOverdueHallEventsCommand());

        count.ShouldBe(1);
        hallEvent.Status.ShouldBe(HallEventStatus.Expired);
        var published = host.Outbox.Single<HallEventExpiredIntegrationEvent>();
        published.EventId.ShouldBe(hallEvent.Id.Value);
        published.HallId.ShouldBe(Hall);
        published.Date.ShouldBe(EventDay);
        published.CreditAmount.ShouldBe(0m);
        host.Credits.All.ShouldBeEmpty();
    }

    [Fact]
    public async Task Expire_UnpaidDepositAfter48h_ExpiresAndTurnsThePartialPaymentIntoCredit_Br17()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.ApprovedEventAsync();
        await host.Mediator.Send(new AccreditDepositCommand(hallEvent.Id.Value, 250m, "BOB", "TX-1"));
        host.Clock.Advance(TimeSpan.FromHours(48));
        host.ResetObservations();

        var count = await host.Mediator.Send(new ExpireOverdueHallEventsCommand());

        count.ShouldBe(1);
        hallEvent.Status.ShouldBe(HallEventStatus.Expired);
        host.Credits.Peek(new CustomerId(TechSol))!.Balance.Amount.ShouldBe(250m);
        host.Outbox.Single<HallEventExpiredIntegrationEvent>().CreditAmount.ShouldBe(250m);
        host.Outbox.Single<CustomerCreditGrantedIntegrationEvent>().Amount.ShouldBe(250m);
    }

    [Fact]
    public async Task Expire_SeveralEventsOfTheSameCustomer_ShareASingleCreditAccount()
    {
        using var host = new ElTestHost();
        var first = await host.ApprovedEventAsync();
        var second = await host.ApprovedEventAsync(message: QuoteAcceptedMessage(date: EventDay.AddDays(2)));
        await host.Mediator.Send(new AccreditDepositCommand(first.Id.Value, 100m, "BOB", "TX-A"));
        await host.Mediator.Send(new AccreditDepositCommand(second.Id.Value, 300m, "BOB", "TX-B"));
        host.Clock.Advance(TimeSpan.FromHours(48));

        (await host.Mediator.Send(new ExpireOverdueHallEventsCommand())).ShouldBe(2);

        host.Credits.All.Count.ShouldBe(1);
        host.Credits.Peek(new CustomerId(TechSol))!.Balance.Amount.ShouldBe(400m);
    }

    [Fact]
    public async Task Expire_NothingOverdue_DoesNotCommit_AndASecondRunIsHarmless()
    {
        using var host = new ElTestHost();
        await host.RequestedEventAsync();
        var commits = host.UnitOfWork.SaveCount;

        (await host.Mediator.Send(new ExpireOverdueHallEventsCommand())).ShouldBe(0);
        host.UnitOfWork.SaveCount.ShouldBe(commits);

        host.Clock.Advance(TimeSpan.FromHours(16));
        (await host.Mediator.Send(new ExpireOverdueHallEventsCommand())).ShouldBe(1);
        (await host.Mediator.Send(new ExpireOverdueHallEventsCommand())).ShouldBe(0);
    }

    [Fact]
    public async Task Expire_ConfirmedEventsAreNeverTouched()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.ConfirmedEventAsync();
        host.Clock.Advance(TimeSpan.FromDays(3));

        (await host.Mediator.Send(new ExpireOverdueHallEventsCommand())).ShouldBe(0);

        hallEvent.Status.ShouldBe(HallEventStatus.Confirmed);
    }
}
