using Nodo.EventLifecycle.Domain;
using Nodo.SharedKernel;
using static Nodo.EventLifecycle.UnitTests.TestData;

namespace Nodo.EventLifecycle.UnitTests;

/// <summary>Behaviour of the event lifecycle: BR-11..BR-23 end to end at domain level.</summary>
public class HallEventLifecycleTests
{
    // ---- BR-11: request created from an accepted quote ----

    [Fact]
    public void FromAcceptedQuote_Br11_CreatesRequestedEventAndRaisesEventRequestCreated()
    {
        var hallEvent = HallEventFactory.FromAcceptedQuote(
            Snapshot(extras: [Equipment("PROJECTOR", "Proyector", 150m)]), NowUtc, Today, Stock);

        hallEvent.Status.ShouldBe(HallEventStatus.Requested);
        hallEvent.CreditGenerated.Amount.ShouldBe(0m);
        hallEvent.Deposit.ShouldBeNull();
        hallEvent.Total.ShouldBe(Total);
        hallEvent.QuoteId.ShouldBe(Quote);
        hallEvent.RescheduleCount.ShouldBe(0);
        hallEvent.DecisionDeadlineUtc.ShouldBe(DecisionDeadline);
        var raised = hallEvent.DequeueDomainEvents().ShouldHaveSingleItem().ShouldBeOfType<EventRequestCreated>();
        raised.EventId.ShouldBe(hallEvent.Id);
        raised.Attendance.ShouldBe(40);
        raised.CustomerId.ShouldBe(Customer);
    }

    // ---- BR-12 / BR-13: Management decision ----

    [Fact]
    public void Approve_Br12_RequestedWithinDay_ApprovesAndSetsDepositDeadline()
    {
        var hallEvent = NewRequested();

        hallEvent.Approve(Management, 30, ApprovalTime);

        hallEvent.Status.ShouldBe(HallEventStatus.ApprovedByManagement);
        hallEvent.DepositDeadlineUtc.ShouldBe(ApprovalTime.AddHours(48));
        hallEvent.Decision!.DecidedBy.ShouldBe(Management);
        hallEvent.Decision.Approved.ShouldBeTrue();
        var raised = hallEvent.DequeueDomainEvents().OfType<HallEventApprovedByManagement>().Single();
        raised.DepositPercentage.ShouldBe(30);
        raised.DepositRequired.Amount.ShouldBe(600m);
        raised.DepositDeadlineUtc.ShouldBe(ApprovalTime.AddHours(48));
    }

    [Fact]
    public void Reject_Br13_ValidReason_RejectsAndRecordsDecision()
    {
        var hallEvent = NewRequested();

        hallEvent.Reject(Management, "El salón estará en mantenimiento.", ApprovalTime);

        hallEvent.Status.ShouldBe(HallEventStatus.Rejected);
        hallEvent.Decision!.Approved.ShouldBeFalse();
        hallEvent.Decision.RejectionReason.ShouldBe("El salón estará en mantenimiento.");
        hallEvent.DequeueDomainEvents().ShouldHaveSingleItem().ShouldBeOfType<EventRequestRejected>();
    }

    // ---- BR-12: expiry of an unattended request ----

    [Fact]
    public void Expire_Br12_RequestedAfterDeadline_ExpiresWithoutCredit()
    {
        var hallEvent = NewRequested();

        hallEvent.Expire(DecisionDeadline);

        hallEvent.Status.ShouldBe(HallEventStatus.Expired);
        hallEvent.CreditGenerated.Amount.ShouldBe(0m);
        hallEvent.DequeueDomainEvents().ShouldHaveSingleItem().ShouldBeOfType<HallEventExpired>();
    }

    [Fact]
    public void Expire_Br12_BeforeDeadline_ThrowsInvalidStateTransition()
    {
        var hallEvent = NewRequested();

        Should.Throw<DomainException>(() => hallEvent.Expire(DecisionDeadline.AddSeconds(-1)))
            .Code.ShouldBe("EVENT.INVALID_STATE_TRANSITION");

        hallEvent.Status.ShouldBe(HallEventStatus.Requested);
    }

    [Fact]
    public void Expire_Br12_CalledTwice_IsIdempotent()
    {
        var hallEvent = NewRequested();
        hallEvent.Expire(DecisionDeadline);
        hallEvent.DequeueDomainEvents();
        var historyCount = hallEvent.History.Count;

        hallEvent.Expire(DecisionDeadline.AddHours(5));

        hallEvent.History.Count.ShouldBe(historyCount);
        hallEvent.DequeueDomainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void Approve_Br12_AfterExpiry_ThrowsInvalidStateTransition()
    {
        var hallEvent = EventIn(HallEventStatus.Expired);

        Should.Throw<DomainException>(() => hallEvent.Approve(Management, 30, ApprovalTime))
            .Code.ShouldBe("EVENT.INVALID_STATE_TRANSITION");
    }

    // ---- BR-15 / BR-16: payments ----

    [Fact]
    public void AccreditDeposit_Br15_PartialPayment_DoesNotConfirm()
    {
        var hallEvent = Approved(); // required 600

        var excess = hallEvent.AccreditDeposit(Bob(300m), "TX-1", ApprovalTime.AddHours(1));

        excess.Amount.ShouldBe(0m);
        hallEvent.Status.ShouldBe(HallEventStatus.ApprovedByManagement);
        hallEvent.Deposit!.Accredited.Amount.ShouldBe(300m);
        hallEvent.Deposit.Pending.Amount.ShouldBe(300m);
        var raised = hallEvent.DequeueDomainEvents().ShouldHaveSingleItem().ShouldBeOfType<GuaranteeDepositAccredited>();
        raised.IsCovered.ShouldBeFalse();
        raised.AccreditedTotal.Amount.ShouldBe(300m);
    }

    [Fact]
    public void AccreditDeposit_Br15_AccumulatedPayments_ConfirmWhenCovered()
    {
        var hallEvent = Approved();

        hallEvent.AccreditDeposit(Bob(250m), "TX-1", ApprovalTime.AddHours(1));
        hallEvent.AccreditDeposit(Bob(350m), "TX-2", ApprovalTime.AddHours(2));

        hallEvent.Status.ShouldBe(HallEventStatus.Confirmed);
        hallEvent.Deposit!.Accredited.Amount.ShouldBe(600m);
        hallEvent.Deposit.TransactionIds.ShouldBe(["TX-1", "TX-2"]);
    }

    [Fact]
    public void AccreditDeposit_Br16_CoveringPayment_ConfirmsInTheSameCommandAndRaisesBothEvents()
    {
        var hallEvent = Approved(extras: [Equipment("PROJECTOR", "Proyector", 150m)]);

        hallEvent.AccreditDeposit(Bob(600m), "TX-1", ApprovalTime.AddHours(1));

        hallEvent.Status.ShouldBe(HallEventStatus.Confirmed);
        hallEvent.DepositDeadlineUtc.ShouldBeNull();
        var events = hallEvent.DequeueDomainEvents();
        events.Count.ShouldBe(2);
        events[0].ShouldBeOfType<GuaranteeDepositAccredited>().IsCovered.ShouldBeTrue();
        var confirmed = events[1].ShouldBeOfType<HallEventFirmlyConfirmed>();
        confirmed.Total.ShouldBe(Total);
        confirmed.DepositAccredited.Amount.ShouldBe(600m);
        confirmed.ExtraServices.Single().Code.ShouldBe("PROJECTOR");
    }

    [Fact]
    public void AccreditDeposit_Br15_PaymentAboveRequired_ReturnsExcessAndCapsTheDeposit()
    {
        var hallEvent = Approved(); // required 600
        hallEvent.AccreditDeposit(Bob(500m), "TX-1", ApprovalTime.AddHours(1));

        var excess = hallEvent.AccreditDeposit(Bob(300m), "TX-2", ApprovalTime.AddHours(2));

        excess.Amount.ShouldBe(200m);
        hallEvent.Status.ShouldBe(HallEventStatus.Confirmed);
        hallEvent.Deposit!.Accredited.Amount.ShouldBe(600m); // the excess becomes customer credit, not counted twice
    }

    [Fact]
    public void AccreditDeposit_Br15_SinglePaymentFarAboveRequired_ReturnsTheWholeExcess()
    {
        var hallEvent = Approved();

        var excess = hallEvent.AccreditDeposit(Bob(1000m), "TX-1", ApprovalTime.AddHours(1));

        excess.Amount.ShouldBe(400m);
        hallEvent.Deposit!.Accredited.Amount.ShouldBe(600m);
    }

    // ---- BR-20: extra services ----

    [Fact]
    public void AssignExtraServices_Br20_Confirmed_AssignsOnceAndIsIdempotent()
    {
        var hallEvent = Confirmed(extras: [Equipment("PROJECTOR", "Proyector", 150m)]);

        hallEvent.AssignExtraServices(NowUtc.AddHours(3));
        hallEvent.AssignExtraServices(NowUtc.AddHours(4));

        hallEvent.ExtraServicesAssigned.ShouldBeTrue();
        hallEvent.DequeueDomainEvents().ShouldHaveSingleItem().ShouldBeOfType<ExtraServicesAssigned>();
    }

    // ---- BR-18 / BR-19: execution ----

    [Fact]
    public void Complete_Br19_InProgress_ExposesRemainingToSettle()
    {
        var hallEvent = EventIn(HallEventStatus.InProgress); // total 2000, deposit 600

        hallEvent.Complete(hallEvent.Slot.EndUtc, Operations);

        hallEvent.Status.ShouldBe(HallEventStatus.Completed);
        hallEvent.RemainingToSettle!.Amount.ShouldBe(1400m);
        var raised = hallEvent.DequeueDomainEvents().ShouldHaveSingleItem().ShouldBeOfType<HallEventCompleted>();
        raised.RemainingToSettle.Amount.ShouldBe(1400m);
        raised.DepositAccredited.Amount.ShouldBe(600m);
    }

    [Fact]
    public void Start_Br18_InsideWindow_RecordsOperatorAndRaisesEvent()
    {
        var hallEvent = Confirmed();

        hallEvent.Start(hallEvent.Slot.StartUtc, Operations);

        hallEvent.Status.ShouldBe(HallEventStatus.InProgress);
        hallEvent.DequeueDomainEvents().ShouldHaveSingleItem().ShouldBeOfType<HallEventStarted>().StartedBy.ShouldBe(Operations);
        hallEvent.History.Last().Actor.ShouldBe(Operations);
    }

    [Fact]
    public void Start_Br18_EventNotConfirmed_ThrowsInvalidStateTransition() =>
        Should.Throw<DomainException>(() => Approved().Start(NowUtc, Operations))
            .Code.ShouldBe("EVENT.INVALID_STATE_TRANSITION");

    // ---- BR-21: cancellation ----

    [Fact]
    public void Cancel_Br21_Requested_CancelsWithoutCreditAndStillRaisesEvent()
    {
        var hallEvent = NewRequested();

        hallEvent.Cancel(Reception, "Duplicado", NowUtc.AddHours(1));

        hallEvent.Status.ShouldBe(HallEventStatus.Cancelled);
        hallEvent.CreditGenerated.Amount.ShouldBe(0m);
        hallEvent.DequeueDomainEvents().ShouldHaveSingleItem().ShouldBeOfType<HallEventCancelledWithCredit>()
            .CreditAmount.Amount.ShouldBe(0m);
    }

    [Fact]
    public void Cancel_Br21_NoMethodRefundsMoney()
    {
        var methodNames = typeof(HallEvent).GetMethods().Select(m => m.Name);

        methodNames.ShouldNotContain(n => n.Contains("Refund") || n.Contains("Reimburse") || n.Contains("Withdraw"));
    }

    // ---- BR-17: expiry of the deposit ----

    [Fact]
    public void Expire_Br17_ApprovedWithoutPayment_ExpiresWithoutCredit()
    {
        var hallEvent = Approved();

        hallEvent.Expire(hallEvent.DepositDeadlineUtc!.Value);

        hallEvent.Status.ShouldBe(HallEventStatus.Expired);
        hallEvent.CreditGenerated.Amount.ShouldBe(0m);
    }

    [Fact]
    public void Expire_Br17_ApprovedWithPartialPayment_TurnsWhatWasPaidIntoCredit()
    {
        var hallEvent = Approved();
        hallEvent.AccreditDeposit(Bob(250m), "TX-1", ApprovalTime.AddHours(1));
        hallEvent.DequeueDomainEvents();

        hallEvent.Expire(hallEvent.DepositDeadlineUtc!.Value);

        hallEvent.Status.ShouldBe(HallEventStatus.Expired);
        hallEvent.CreditGenerated.Amount.ShouldBe(250m);
        hallEvent.DepositDeadlineUtc.ShouldBeNull();
        hallEvent.DequeueDomainEvents().ShouldHaveSingleItem().ShouldBeOfType<HallEventExpired>()
            .CreditAmount.Amount.ShouldBe(250m);
    }

    [Fact]
    public void Expire_Br17_OneSecondBeforeDeadline_ThrowsInvalidStateTransition()
    {
        var hallEvent = Approved();

        Should.Throw<DomainException>(() => hallEvent.Expire(hallEvent.DepositDeadlineUtc!.Value.AddSeconds(-1)))
            .Code.ShouldBe("EVENT.INVALID_STATE_TRANSITION");
    }

    // ---- BR-23: reschedule ----

    [Fact]
    public void Reschedule_Br23_CreditCoversDeposit_ConfirmsWithoutNewApproval()
    {
        var hallEvent = EventIn(HallEventStatus.Cancelled); // 600 credit generated, deposit 30 % = 600
        var newSlot = Slot(10, 14, Today.AddDays(20));

        hallEvent.Reschedule(newSlot, Bob(600m), Reception, NowUtc.AddHours(6), Today);

        hallEvent.Status.ShouldBe(HallEventStatus.Confirmed);
        hallEvent.Slot.ShouldBe(newSlot);
        hallEvent.Deposit!.IsCovered.ShouldBeTrue();
        hallEvent.DepositDeadlineUtc.ShouldBeNull();
        hallEvent.CreditGenerated.Amount.ShouldBe(0m);
        hallEvent.RescheduleCount.ShouldBe(1);
        hallEvent.ExtraServicesAssigned.ShouldBeFalse();
        var events = hallEvent.DequeueDomainEvents();
        events.OfType<HallEventRescheduledWithCredit>().Single().ResultingStatus.ShouldBe(HallEventStatus.Confirmed);
        events.OfType<HallEventFirmlyConfirmed>().Single().Date.ShouldBe(newSlot.Date);
    }

    [Fact]
    public void Reschedule_Br23_CreditDoesNotCoverDeposit_LeavesApprovedWith48hToPayTheDifference()
    {
        var hallEvent = EventIn(HallEventStatus.Cancelled);
        var rescheduledAt = NowUtc.AddHours(6);

        hallEvent.Reschedule(Slot(10, 14, Today.AddDays(20)), Bob(400m), Reception, rescheduledAt, Today);

        hallEvent.Status.ShouldBe(HallEventStatus.ApprovedByManagement);
        hallEvent.Deposit!.Accredited.Amount.ShouldBe(400m);
        hallEvent.Deposit.Pending.Amount.ShouldBe(200m);
        hallEvent.DepositDeadlineUtc.ShouldBe(rescheduledAt.AddHours(48));
        var events = hallEvent.DequeueDomainEvents();
        events.OfType<HallEventFirmlyConfirmed>().ShouldBeEmpty();
        var rescheduled = events.OfType<HallEventRescheduledWithCredit>().Single();
        rescheduled.ResultingStatus.ShouldBe(HallEventStatus.ApprovedByManagement);
        rescheduled.DepositPending.Amount.ShouldBe(200m);                       // what the payment link must ask for
        rescheduled.DepositDeadlineUtc.ShouldBe(rescheduledAt.AddHours(48));
    }

    [Fact]
    public void Reschedule_Br23_ThenPayingTheDifference_ConfirmsTheEvent()
    {
        var hallEvent = EventIn(HallEventStatus.Cancelled);
        hallEvent.Reschedule(Slot(10, 14, Today.AddDays(20)), Bob(400m), Reception, NowUtc.AddHours(6), Today);

        hallEvent.AccreditDeposit(Bob(200m), "TX-DIFF", NowUtc.AddHours(7));

        hallEvent.Status.ShouldBe(HallEventStatus.Confirmed);
    }

    [Fact]
    public void Reschedule_Br23_ThenLettingTheNewDepositExpire_ExpiresAndKeepsPartialAsCredit()
    {
        var hallEvent = EventIn(HallEventStatus.Cancelled);
        hallEvent.Reschedule(Slot(10, 14, Today.AddDays(20)), Bob(400m), Reception, NowUtc.AddHours(6), Today);

        hallEvent.Expire(NowUtc.AddHours(6).AddHours(48));

        hallEvent.Status.ShouldBe(HallEventStatus.Expired);
        hallEvent.CreditGenerated.Amount.ShouldBe(400m);
    }

    [Fact]
    public void Reschedule_Br23_ConfirmedAgainThenCancelled_GeneratesCreditFromTheNewDeposit()
    {
        var hallEvent = EventIn(HallEventStatus.Cancelled);
        hallEvent.Reschedule(Slot(10, 14, Today.AddDays(20)), Bob(600m), Reception, NowUtc.AddHours(6), Today);

        hallEvent.Cancel(Reception, "Otra vez", NowUtc.AddHours(7));

        hallEvent.CreditGenerated.Amount.ShouldBe(600m);
        hallEvent.Status.ShouldBe(HallEventStatus.Cancelled);
    }

    // ---- History: every transition leaves a trace ----

    [Fact]
    public void History_FullLifecycle_RecordsEveryTransitionWithActor()
    {
        var hallEvent = NewRequested();
        hallEvent.Approve(Management, 30, ApprovalTime);
        hallEvent.AccreditDeposit(Bob(600m), "TX-1", ApprovalTime.AddHours(1));
        hallEvent.Start(hallEvent.Slot.StartUtc, Operations);
        hallEvent.Complete(hallEvent.Slot.EndUtc, Operations);

        hallEvent.History.Select(h => h.Status).ShouldBe(
        [
            HallEventStatus.Requested,
            HallEventStatus.ApprovedByManagement,
            HallEventStatus.Confirmed,
            HallEventStatus.InProgress,
            HallEventStatus.Completed
        ]);
        hallEvent.History[1].Actor.ShouldBe(Management);
        hallEvent.History[1].OccurredAtUtc.ShouldBe(ApprovalTime);
        hallEvent.History.ShouldAllBe(h => h.Note.Length > 0);
    }

    [Fact]
    public void History_CancellationAndReschedule_AreRecorded()
    {
        var hallEvent = EventIn(HallEventStatus.Cancelled);
        hallEvent.Reschedule(Slot(10, 14, Today.AddDays(20)), Bob(600m), Reception, NowUtc.AddHours(6), Today);

        hallEvent.History.Select(h => h.Status).TakeLast(2).ShouldBe(
            [HallEventStatus.Cancelled, HallEventStatus.Confirmed]);
        hallEvent.History.Last().Note.ShouldContain("Reprogramado");
    }

    [Fact]
    public void History_ExposedAsReadOnly()
    {
        var hallEvent = NewRequested();

        (hallEvent.History is List<HistoryEntry>).ShouldBeFalse();
        Should.Throw<NotSupportedException>(() => ((IList<HistoryEntry>)hallEvent.History).Clear());
    }
}
