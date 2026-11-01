using FluentValidation;
using Nodo.BuildingBlocks.Application;
using Nodo.EventLifecycle.Application;
using Nodo.EventLifecycle.Contracts;
using Nodo.EventLifecycle.Domain;
using Nodo.SharedKernel;
using static Nodo.EventLifecycle.UnitTests.UseCases.ElTestHost;

namespace Nodo.EventLifecycle.UnitTests.UseCases;

public class DecisionAndDepositTests
{
    // ---- ApproveEventRequest ----

    [Fact]
    public async Task Approve_RequestedEvent_ApprovesAndPublishesHallEventApprovedByManagement()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.RequestedEventAsync();
        host.ResetObservations();

        var result = await host.Mediator.Send(new ApproveEventRequestCommand(hallEvent.Id.Value, 30, Management));

        result.Status.ShouldBe("ApprovedByManagement");
        var published = host.Outbox.Single<HallEventApprovedByManagementIntegrationEvent>();
        published.EventId.ShouldBe(hallEvent.Id.Value);
        published.ApprovedBy.ShouldBe(Management);
        published.DepositPercentage.ShouldBe(30);
        published.DepositRequired.ShouldBe(600m);
        published.DepositDeadlineUtc.ShouldBe(host.Clock.UtcNow.AddHours(48));
    }

    [Fact]
    public async Task Approve_PercentageOutOfRange_ThrowsAndSavesNothing()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.RequestedEventAsync();
        host.ResetObservations();
        var commits = host.UnitOfWork.SaveCount;

        var ex = await Should.ThrowAsync<DomainException>(
            () => host.Mediator.Send(new ApproveEventRequestCommand(hallEvent.Id.Value, 19, Management)));

        ex.Code.ShouldBe("EVENT.DEPOSIT_PERCENTAGE_OUT_OF_RANGE");
        host.UnitOfWork.SaveCount.ShouldBe(commits);
        host.Outbox.Events.ShouldBeEmpty();
        hallEvent.Status.ShouldBe(HallEventStatus.Requested);
    }

    [Fact]
    public async Task Approve_AfterTheDayOfTheRequest_ThrowsDecisionDeadlineExceeded()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.RequestedEventAsync();
        host.Clock.Advance(TimeSpan.FromHours(16)); // 04:00 UTC next day = 00:00 local

        var ex = await Should.ThrowAsync<DomainException>(
            () => host.Mediator.Send(new ApproveEventRequestCommand(hallEvent.Id.Value, 30, Management)));

        ex.Code.ShouldBe("EVENT.DECISION_DEADLINE_EXCEEDED");
    }

    [Fact]
    public async Task Approve_UnknownEvent_ThrowsNotFound()
    {
        using var host = new ElTestHost();

        await Should.ThrowAsync<NotFoundException>(
            () => host.Mediator.Send(new ApproveEventRequestCommand(Guid.NewGuid(), 30, Management)));
    }

    [Fact]
    public async Task Approve_MissingApprover_FailsShapeValidation()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.RequestedEventAsync();

        await Should.ThrowAsync<ValidationException>(
            () => host.Mediator.Send(new ApproveEventRequestCommand(hallEvent.Id.Value, 30, " ")));
    }

    // ---- RejectEventRequest ----

    [Fact]
    public async Task Reject_ValidReason_RejectsAndPublishesEventRequestRejected()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.RequestedEventAsync();
        host.ResetObservations();

        var result = await host.Mediator.Send(
            new RejectEventRequestCommand(hallEvent.Id.Value, "El salón estará en mantenimiento.", Management));

        result.Status.ShouldBe("Rejected");
        var published = host.Outbox.Single<EventRequestRejectedIntegrationEvent>();
        published.EventId.ShouldBe(hallEvent.Id.Value);
        published.HallId.ShouldBe(Hall);
        published.Date.ShouldBe(EventDay);
        published.RejectedBy.ShouldBe(Management);
        published.Reason.ShouldBe("El salón estará en mantenimiento.");
    }

    [Fact]
    public async Task Reject_ReasonTooShort_ThrowsRejectionReasonRequired()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.RequestedEventAsync();
        var commits = host.UnitOfWork.SaveCount;

        var ex = await Should.ThrowAsync<DomainException>(
            () => host.Mediator.Send(new RejectEventRequestCommand(hallEvent.Id.Value, "corto", Management)));

        ex.Code.ShouldBe("EVENT.REJECTION_REASON_REQUIRED");
        host.UnitOfWork.SaveCount.ShouldBe(commits);
    }

    // ---- AccreditDeposit ----

    [Fact]
    public async Task AccreditDeposit_PartialPayment_DoesNotConfirm()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.ApprovedEventAsync();
        host.ResetObservations();

        var result = await host.Mediator.Send(new AccreditDepositCommand(hallEvent.Id.Value, 250m, "BOB", "TX-1"));

        result.Status.ShouldBe("ApprovedByManagement");
        result.IsCovered.ShouldBeFalse();
        result.AccreditedTotal.ShouldBe(250m);
        result.Pending.ShouldBe(350m);
        result.ExcessGrantedAsCredit.ShouldBe(0m);
        var published = host.Outbox.Single<GuaranteeDepositAccreditedIntegrationEvent>();
        published.Amount.ShouldBe(250m);
        published.TransactionId.ShouldBe("TX-1");
        published.AccreditedTotal.ShouldBe(250m);
        published.IsCovered.ShouldBeFalse();
        host.Outbox.Count<HallEventFirmlyConfirmedIntegrationEvent>().ShouldBe(0);
    }

    [Fact]
    public async Task AccreditDeposit_CoveringPayment_ConfirmsAndPublishesEverythingInOneCommit()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.ApprovedEventAsync(message: QuoteAcceptedMessage(extras: [Projector(), CoffeeBreak()]));
        host.ResetObservations();
        var commits = host.UnitOfWork.SaveCount;

        var result = await host.Mediator.Send(new AccreditDepositCommand(hallEvent.Id.Value, 600m, "BOB", "TX-1"));

        result.Status.ShouldBe("Confirmed");
        result.IsCovered.ShouldBeTrue();
        host.UnitOfWork.SaveCount.ShouldBe(commits + 1); // one transaction (BR-16)
        host.Outbox.Single<GuaranteeDepositAccreditedIntegrationEvent>().IsCovered.ShouldBeTrue();
        var confirmed = host.Outbox.Single<HallEventFirmlyConfirmedIntegrationEvent>();
        confirmed.EventId.ShouldBe(hallEvent.Id.Value);
        confirmed.HallId.ShouldBe(Hall);
        confirmed.Date.ShouldBe(EventDay);
        confirmed.Start.ShouldBe(new TimeOnly(9, 0));
        confirmed.End.ShouldBe(new TimeOnly(13, 0));
        confirmed.Attendance.ShouldBe(40);
        confirmed.CustomerId.ShouldBe(TechSol);
        confirmed.Total.ShouldBe(2000m);
        confirmed.DepositAccredited.ShouldBe(600m);
        confirmed.ExtraServices.Select(e => e.Code).ShouldBe(["PROJECTOR", "COFFEE_BREAK"], ignoreOrder: true);
    }

    [Fact]
    public async Task AccreditDeposit_Confirmation_AssignsTheExtraServicesInTheSameTransaction_Br20()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.ApprovedEventAsync(message: QuoteAcceptedMessage(extras: [Projector(), CoffeeBreak()]));
        host.ResetObservations();

        await host.Mediator.Send(new AccreditDepositCommand(hallEvent.Id.Value, 600m, "BOB", "TX-1"));

        hallEvent.ExtraServicesAssigned.ShouldBeTrue();
        var assigned = host.Outbox.Single<ExtraServicesAssignedIntegrationEvent>();
        assigned.EventId.ShouldBe(hallEvent.Id.Value);
        assigned.ExtraServices.Select(e => (e.Type, e.Code, e.Quantity, e.UnitCost)).ShouldBe(
            [("OwnEquipment", "PROJECTOR", 1, 150m), ("ExternalCatering", "COFFEE_BREAK", 40, 35m)], ignoreOrder: true);
    }

    [Fact]
    public async Task AccreditDeposit_PaymentsAccumulateUntilTheDepositIsCovered()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.ApprovedEventAsync();

        var first = await host.Mediator.Send(new AccreditDepositCommand(hallEvent.Id.Value, 250m, "BOB", "TX-1"));
        var second = await host.Mediator.Send(new AccreditDepositCommand(hallEvent.Id.Value, 350m, "BOB", "TX-2"));

        first.Status.ShouldBe("ApprovedByManagement");
        second.Status.ShouldBe("Confirmed");
        second.AccreditedTotal.ShouldBe(600m);
    }

    [Fact]
    public async Task AccreditDeposit_PaymentAboveRequired_GrantsTheExcessAsCustomerCredit_Br15()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.ApprovedEventAsync();
        host.ResetObservations();
        var commits = host.UnitOfWork.SaveCount;

        var result = await host.Mediator.Send(new AccreditDepositCommand(hallEvent.Id.Value, 800m, "BOB", "TX-1"));

        result.Status.ShouldBe("Confirmed");
        result.AccreditedTotal.ShouldBe(600m);
        result.ExcessGrantedAsCredit.ShouldBe(200m);
        host.UnitOfWork.SaveCount.ShouldBe(commits + 1); // event and credit saved together
        var credit = host.Credits.Peek(new CustomerId(TechSol))!;
        credit.Balance.Amount.ShouldBe(200m);
        var granted = host.Outbox.Single<CustomerCreditGrantedIntegrationEvent>();
        granted.CustomerId.ShouldBe(TechSol);
        granted.EventId.ShouldBe(hallEvent.Id.Value);
        granted.Amount.ShouldBe(200m);
        granted.Balance.ShouldBe(200m);
    }

    [Fact]
    public async Task AccreditDeposit_ExcessForACustomerWithCredit_IncrementsTheExistingCredit()
    {
        using var host = new ElTestHost();
        await host.CancelledEventWithCreditAsync(); // TechSol now has 600 Bs of credit
        var next = await host.ApprovedEventAsync(message: QuoteAcceptedMessage(date: EventDay.AddDays(3)));

        await host.Mediator.Send(new AccreditDepositCommand(next.Id.Value, 700m, "BOB", "TX-X"));

        host.Credits.All.Count.ShouldBe(1);
        host.Credits.Peek(new CustomerId(TechSol))!.Balance.Amount.ShouldBe(700m); // 600 + 100
    }

    [Fact]
    public async Task AccreditDeposit_ExactAmount_DoesNotCreateCustomerCredit()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.ApprovedEventAsync();

        await host.Mediator.Send(new AccreditDepositCommand(hallEvent.Id.Value, 600m, "BOB", "TX-1"));

        host.Credits.All.ShouldBeEmpty();
    }

    [Fact]
    public async Task AccreditDeposit_SameTransactionTwice_IsIdempotent()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.ApprovedEventAsync();
        await host.Mediator.Send(new AccreditDepositCommand(hallEvent.Id.Value, 250m, "BOB", "TX-1"));
        host.ResetObservations();

        var again = await host.Mediator.Send(new AccreditDepositCommand(hallEvent.Id.Value, 250m, "BOB", "TX-1"));

        again.AccreditedTotal.ShouldBe(250m);
        again.ExcessGrantedAsCredit.ShouldBe(0m);
        host.Outbox.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task AccreditDeposit_RepeatedWebhookAfterConfirmation_IsStillHarmless()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.ApprovedEventAsync();
        await host.Mediator.Send(new AccreditDepositCommand(hallEvent.Id.Value, 600m, "BOB", "TX-1"));
        host.ResetObservations();

        var again = await host.Mediator.Send(new AccreditDepositCommand(hallEvent.Id.Value, 600m, "BOB", "TX-1"));

        again.Status.ShouldBe("Confirmed");
        host.Outbox.Events.ShouldBeEmpty();
        host.Credits.All.ShouldBeEmpty();
    }

    [Fact]
    public async Task AccreditDeposit_OtherCurrency_ThrowsCurrencyMismatchAndSavesNothing()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.ApprovedEventAsync();
        host.ResetObservations();
        var commits = host.UnitOfWork.SaveCount;

        var ex = await Should.ThrowAsync<DomainException>(
            () => host.Mediator.Send(new AccreditDepositCommand(hallEvent.Id.Value, 100m, "USD", "TX-USD")));

        ex.Code.ShouldBe("MONEY.CURRENCY_MISMATCH");
        host.UnitOfWork.SaveCount.ShouldBe(commits);
        host.Outbox.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task AccreditDeposit_NegativeAmount_ThrowsNegativeAmount()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.ApprovedEventAsync();

        var ex = await Should.ThrowAsync<DomainException>(
            () => host.Mediator.Send(new AccreditDepositCommand(hallEvent.Id.Value, -5m, "BOB", "TX-NEG")));

        ex.Code.ShouldBe("MONEY.NEGATIVE_AMOUNT");
    }

    [Fact]
    public async Task AccreditDeposit_EventNotApprovedYet_ThrowsDepositNotAccepted()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.RequestedEventAsync();

        var ex = await Should.ThrowAsync<DomainException>(
            () => host.Mediator.Send(new AccreditDepositCommand(hallEvent.Id.Value, 100m, "BOB", "TX-1")));

        ex.Code.ShouldBe("EVENT.DEPOSIT_NOT_ACCEPTED");
    }

    [Fact]
    public async Task AccreditDeposit_AfterTheDepositDeadline_ThrowsDepositNotAccepted()
    {
        using var host = new ElTestHost();
        var hallEvent = await host.ApprovedEventAsync();
        host.Clock.Advance(TimeSpan.FromHours(48));

        var ex = await Should.ThrowAsync<DomainException>(
            () => host.Mediator.Send(new AccreditDepositCommand(hallEvent.Id.Value, 100m, "BOB", "TX-LATE")));

        ex.Code.ShouldBe("EVENT.DEPOSIT_NOT_ACCEPTED");
    }

    [Theory]
    [InlineData("", "BOB")]
    [InlineData("TX-1", "")]
    [InlineData("TX-1", "BOLIVIANOS")]
    public async Task AccreditDeposit_MissingTransactionOrBadCurrencyFormat_FailsShapeValidation(string tx, string currency)
    {
        using var host = new ElTestHost();

        await Should.ThrowAsync<ValidationException>(
            () => host.Mediator.Send(new AccreditDepositCommand(Guid.NewGuid(), 100m, currency, tx)));
    }

    [Fact]
    public async Task AccreditDeposit_UnknownEvent_ThrowsNotFound()
    {
        using var host = new ElTestHost();

        await Should.ThrowAsync<NotFoundException>(
            () => host.Mediator.Send(new AccreditDepositCommand(Guid.NewGuid(), 100m, "BOB", "TX-1")));
    }
}
