using NSubstitute;
using Nodo.EventLifecycle.Application;
using Nodo.EventLifecycle.Contracts;
using Nodo.EventLifecycle.Domain;
using Nodo.HallSchedule.Contracts;
using Nodo.SharedKernel;
using static Nodo.EventLifecycle.UnitTests.UseCases.ElTestHost;

namespace Nodo.EventLifecycle.UnitTests.UseCases;

/// <summary>BR-23: rescheduling a cancelled event with the customer's credit.</summary>
public class RescheduleTests
{
    private static readonly DateOnly NewDay = new(2026, 11, 21);

    private static RescheduleHallEventCommand Command(HallEvent hallEvent, DateOnly? day = null, int start = 10) =>
        new(hallEvent.Id.Value, day ?? NewDay, new TimeOnly(start, 0), Reception);

    /// <summary>Gives TechSol extra credit on top of what the cancellation generated.</summary>
    private static void AddCredit(ElTestHost host, HallEventId eventId, decimal amount)
    {
        var credit = host.Credits.Peek(new CustomerId(TechSol))!;
        credit.Grant(Money.Of(amount), eventId, "Saldo adicional", host.Clock.UtcNow);
        credit.DequeueDomainEvents();
    }

    [Fact]
    public async Task Reschedule_CreditCoversTheDeposit_ConfirmsWithADefinitiveBlockAndKeepsTheSurplus()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.CancelledEventWithCreditAsync(QuoteAcceptedMessage(extras: [Projector(), CoffeeBreak()]));
        AddCredit(host, hallEvent.Id, 200m); // balance 800, deposit 600
        host.ResetObservations();

        var result = await host.Mediator.Send(Command(hallEvent));

        result.Status.ShouldBe("Confirmed");
        result.CreditApplied.ShouldBe(600m);
        result.DepositPending.ShouldBe(0m);
        hallEvent.Slot.Date.ShouldBe(NewDay);
        hallEvent.RescheduleCount.ShouldBe(1);
        host.Credits.Peek(new CustomerId(TechSol))!.Balance.Amount.ShouldBe(200m); // surplus stays as credit
        await host.HallSchedule.Received(1).PlaceDefinitiveBlockAsync(
            hallEvent.Id.Value, Hall, NewDay, new TimeOnly(10, 0), new TimeOnly(14, 0),
            Arg.Is<IReadOnlyCollection<EquipmentRequest>>(e => e.Single().Code == "PROJECTOR"), Arg.Any<CancellationToken>());
        await host.HallSchedule.DidNotReceiveWithAnyArgs()
            .PlaceProvisionalBlockAsync(default, default, default, default, default, default!, default);
    }

    [Fact]
    public async Task Reschedule_CreditCoversTheDeposit_PublishesRescheduledConfirmedCreditAppliedAndAssignedInOneCommit()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.CancelledEventWithCreditAsync(QuoteAcceptedMessage(extras: [Projector()]));
        host.ResetObservations();
        var commits = host.UnitOfWork.SaveCount;

        await host.Mediator.Send(Command(hallEvent));

        host.UnitOfWork.SaveCount.ShouldBe(commits + 1);
        var rescheduled = host.Outbox.Single<HallEventRescheduledWithCreditIntegrationEvent>();
        rescheduled.EventId.ShouldBe(hallEvent.Id.Value);
        rescheduled.NewDate.ShouldBe(NewDay);
        rescheduled.NewStart.ShouldBe(new TimeOnly(10, 0));
        rescheduled.NewEnd.ShouldBe(new TimeOnly(14, 0));
        rescheduled.CreditApplied.ShouldBe(600m);
        rescheduled.ResultingStatus.ShouldBe("Confirmed");
        host.Outbox.Single<HallEventFirmlyConfirmedIntegrationEvent>().Date.ShouldBe(NewDay);
        var applied = host.Outbox.Single<CustomerCreditAppliedIntegrationEvent>();
        applied.Amount.ShouldBe(600m);
        applied.Balance.ShouldBe(0m);
        host.Outbox.Count<ExtraServicesAssignedIntegrationEvent>().ShouldBe(1); // BR-20 again on the new date
    }

    [Fact]
    public async Task Reschedule_CreditDoesNotCoverTheDeposit_LeavesApprovedWithAProvisionalBlock()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.CancelledEventWithCreditAsync();
        host.Credits.Peek(new CustomerId(TechSol))!.Apply(Money.Of(200m), hallEvent.Id, host.Clock.UtcNow); // balance 400
        host.ResetObservations();

        var result = await host.Mediator.Send(Command(hallEvent));

        result.Status.ShouldBe("ApprovedByManagement");
        result.CreditApplied.ShouldBe(400m);
        result.DepositPending.ShouldBe(200m);
        result.DepositDeadlineUtc.ShouldBe(host.Clock.UtcNow.AddHours(48));
        host.Credits.Peek(new CustomerId(TechSol))!.Balance.Amount.ShouldBe(0m);
        await host.HallSchedule.Received(1).PlaceProvisionalBlockAsync(
            hallEvent.Id.Value, Hall, NewDay, new TimeOnly(10, 0), new TimeOnly(14, 0),
            Arg.Any<IReadOnlyCollection<EquipmentRequest>>(), Arg.Any<CancellationToken>());
        await host.HallSchedule.DidNotReceiveWithAnyArgs()
            .PlaceDefinitiveBlockAsync(default, default, default, default, default, default!, default);
        host.Outbox.Single<HallEventRescheduledWithCreditIntegrationEvent>().ResultingStatus.ShouldBe("ApprovedByManagement");
        host.Outbox.Count<HallEventFirmlyConfirmedIntegrationEvent>().ShouldBe(0);
    }

    [Fact]
    public async Task Reschedule_ThenPayingTheDifference_ConfirmsTheEvent()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.CancelledEventWithCreditAsync();
        host.Credits.Peek(new CustomerId(TechSol))!.Apply(Money.Of(200m), hallEvent.Id, host.Clock.UtcNow);
        await host.Mediator.Send(Command(hallEvent));

        var payment = await host.Mediator.Send(new AccreditDepositCommand(hallEvent.Id.Value, 200m, "BOB", "TX-DIFF"));

        payment.Status.ShouldBe("Confirmed");
    }

    [Fact]
    public async Task Reschedule_NewSlotNotAvailable_FailsBeforeTouchingAnythingAndSavesNothing()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.CancelledEventWithCreditAsync();
        host.ResetObservations();
        var commits = host.UnitOfWork.SaveCount;
        host.HallScheduleIsBusy("SCHEDULE.SLOT_OVERLAP", "Otro evento ocupa esa franja.");

        var ex = await Should.ThrowAsync<DomainException>(() => host.Mediator.Send(Command(hallEvent)));

        ex.Code.ShouldBe("SCHEDULE.SLOT_OVERLAP");
        hallEvent.Status.ShouldBe(HallEventStatus.Cancelled);
        hallEvent.Slot.Date.ShouldBe(EventDay);
        hallEvent.RescheduleCount.ShouldBe(0);
        hallEvent.CreditGenerated.Amount.ShouldBe(600m);
        host.Credits.Peek(new CustomerId(TechSol))!.Balance.Amount.ShouldBe(600m); // credit not consumed
        host.UnitOfWork.SaveCount.ShouldBe(commits);
        host.Outbox.Events.ShouldBeEmpty();
        await host.HallSchedule.DidNotReceiveWithAnyArgs()
            .PlaceDefinitiveBlockAsync(default, default, default, default, default, default!, default);
    }

    [Fact]
    public async Task Reschedule_TheReservationLosesARace_ThrowsAndNothingIsPersisted()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.CancelledEventWithCreditAsync();
        host.ResetObservations();
        var commits = host.UnitOfWork.SaveCount;
        host.HallScheduleRejectsReservations("SCHEDULE.SLOT_OVERLAP", "Otro evento la tomó.");

        var ex = await Should.ThrowAsync<DomainException>(() => host.Mediator.Send(Command(hallEvent)));

        ex.Code.ShouldBe("SCHEDULE.SLOT_OVERLAP");
        host.UnitOfWork.SaveCount.ShouldBe(commits); // the transaction was never committed
        host.Outbox.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reschedule_EventThatNeverGeneratedCredit_FailsWithoutCallingTheSchedule()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.RequestedEventAsync();
        await host.Mediator.Send(new CancelHallEventCommand(hallEvent.Id.Value, "x", Reception)); // nothing was paid
        host.ResetObservations();

        var ex = await Should.ThrowAsync<DomainException>(() => host.Mediator.Send(Command(hallEvent)));

        ex.Code.ShouldBe("EVENT.RESCHEDULE_NOT_ALLOWED");
        await host.HallSchedule.DidNotReceiveWithAnyArgs()
            .CheckAvailabilityAsync(default, default, default, default, default!, default, default);
    }

    [Fact]
    public async Task Reschedule_EventNotCancelled_Fails()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.ConfirmedEventAsync();

        var ex = await Should.ThrowAsync<DomainException>(() => host.Mediator.Send(Command(hallEvent)));

        ex.Code.ShouldBe("EVENT.RESCHEDULE_NOT_ALLOWED");
    }

    [Theory]
    [InlineData(10, 14)] // the original event lasts 4 h (09-13)
    [InlineData(8, 12)]
    public async Task Reschedule_OnlyTheStartIsChosen_TheNewSlotKeepsTheOriginalDuration_Br23(int start, int expectedEnd)
    {
        using var host = new ElTestHost();
        var hallEvent = await host.CancelledEventWithCreditAsync();

        await host.Mediator.Send(Command(hallEvent, start: start));

        hallEvent.Slot.Start.ShouldBe(new TimeOnly(start, 0));
        hallEvent.Slot.End.ShouldBe(new TimeOnly(expectedEnd, 0));
        await host.HallSchedule.Received(1).PlaceDefinitiveBlockAsync(
            hallEvent.Id.Value, Hall, NewDay, new TimeOnly(start, 0), new TimeOnly(expectedEnd, 0),
            Arg.Any<IReadOnlyCollection<EquipmentRequest>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reschedule_StartThatPushesTheEndPastMidnight_FailsOutsideOperatingHoursAndSavesNothing()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.CancelledEventWithCreditAsync();
        var commits = host.UnitOfWork.SaveCount;

        var ex = await Should.ThrowAsync<DomainException>(() => host.Mediator.Send(Command(hallEvent, start: 21))); // 21 + 4 h

        ex.Code.ShouldBe("QUOTE.SLOT_OUTSIDE_OPERATING_HOURS");
        host.UnitOfWork.SaveCount.ShouldBe(commits);
    }

    [Fact]
    public async Task Reschedule_StartAtHalfPast_FailsAsNotExactHour()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.CancelledEventWithCreditAsync();
        var command = Command(hallEvent) with { NewStart = new TimeOnly(10, 30) };

        var ex = await Should.ThrowAsync<DomainException>(() => host.Mediator.Send(command));

        ex.Code.ShouldBe("EVENT.DURATION_OUT_OF_RANGE");
    }

    [Fact]
    public async Task Reschedule_DateOutsideTheBookingWindow_FailsWithDateOutOfRange()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.CancelledEventWithCreditAsync();

        var ex = await Should.ThrowAsync<DomainException>(
            () => host.Mediator.Send(Command(hallEvent, day: new DateOnly(2026, 11, 3))));

        ex.Code.ShouldBe("QUOTE.DATE_OUT_OF_RANGE");
    }

    [Fact]
    public async Task Reschedule_CustomerCreditNoLongerExists_FailsWithCreditInsufficient()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.CancelledEventWithCreditAsync();
        host.Credits.Remove(new CustomerId(TechSol));
        var commits = host.UnitOfWork.SaveCount;

        var ex = await Should.ThrowAsync<DomainException>(() => host.Mediator.Send(Command(hallEvent)));

        ex.Code.ShouldBe("CREDIT.INSUFFICIENT");
        host.UnitOfWork.SaveCount.ShouldBe(commits);
        host.Credits.All.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reschedule_ChecksAvailabilityIgnoringTheEventsOwnBlock()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.CancelledEventWithCreditAsync();
        host.ResetObservations();

        await host.Mediator.Send(Command(hallEvent));

        await host.HallSchedule.Received(1).CheckAvailabilityAsync(
            Hall, NewDay, new TimeOnly(10, 0), new TimeOnly(14, 0), Arg.Any<IReadOnlyCollection<EquipmentRequest>>(),
            hallEvent.Id.Value, Arg.Any<CancellationToken>());
    }
}
