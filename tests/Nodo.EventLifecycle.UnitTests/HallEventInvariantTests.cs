using Nodo.EventLifecycle.Domain;
using Nodo.SharedKernel;
using static Nodo.EventLifecycle.UnitTests.TestData;

namespace Nodo.EventLifecycle.UnitTests;

/// <summary>Invariants INV-01..INV-13 of the HallEvent aggregate (business-rules.md §9). Codes are asserted literally.</summary>
public class HallEventInvariantTests
{
    // ================= INV-01 / BR-01: duration 1..8 whole hours =================

    [Theory]
    [InlineData(9, 10)] // 1 h
    [InlineData(9, 17)] // 8 h
    public void TimeSlotCreate_Inv01_DurationWithinRange_Succeeds(int start, int end) =>
        Slot(start, end).DurationHours.ShouldBe(end - start);

    [Theory]
    [InlineData(9, 18)] // 9 h
    [InlineData(9, 9)]
    [InlineData(12, 10)]
    public void TimeSlotCreate_Inv01_DurationOutOfRange_ThrowsDurationOutOfRange(int start, int end) =>
        Should.Throw<DomainException>(() => Slot(start, end)).Code.ShouldBe("EVENT.DURATION_OUT_OF_RANGE");

    [Fact]
    public void TimeSlotCreate_Inv01_NotExactHour_ThrowsDurationOutOfRange() =>
        Should.Throw<DomainException>(() => TimeSlot.Create(EventDay, new TimeOnly(9, 15), new TimeOnly(12, 0)))
            .Code.ShouldBe("EVENT.DURATION_OUT_OF_RANGE");

    // ================= INV-02 / BR-14: deposit between 20 % and 50 % =================

    [Theory]
    [InlineData(20, 400)]
    [InlineData(30, 600)]
    [InlineData(50, 1000)]
    public void Approve_Inv02_PercentageWithinRange_SetsRequiredDeposit(int percentage, double required)
    {
        var hallEvent = NewRequested();

        hallEvent.Approve(Management, percentage, ApprovalTime);

        hallEvent.Status.ShouldBe(HallEventStatus.ApprovedByManagement);
        hallEvent.Deposit!.Percentage.ShouldBe(percentage);
        hallEvent.Deposit.Required.Amount.ShouldBe((decimal)required);
    }

    [Theory]
    [InlineData(19)]
    [InlineData(51)]
    [InlineData(0)]
    [InlineData(100)]
    public void Approve_Inv02_PercentageOutOfRange_ThrowsAndLeavesEventUntouched(int percentage)
    {
        var hallEvent = NewRequested();

        var ex = Should.Throw<DomainException>(() => hallEvent.Approve(Management, percentage, ApprovalTime));

        ex.Code.ShouldBe("EVENT.DEPOSIT_PERCENTAGE_OUT_OF_RANGE");
        hallEvent.Status.ShouldBe(HallEventStatus.Requested);
        hallEvent.Deposit.ShouldBeNull();
        hallEvent.Decision.ShouldBeNull();
        hallEvent.History.Count.ShouldBe(1);
        hallEvent.DequeueDomainEvents().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(19)]
    [InlineData(51)]
    public void GuaranteeDepositCreate_Inv02_PercentageOutOfRange_ThrowsDepositPercentageOutOfRange(int percentage) =>
        Should.Throw<DomainException>(() => GuaranteeDeposit.Create(percentage, Total))
            .Code.ShouldBe("EVENT.DEPOSIT_PERCENTAGE_OUT_OF_RANGE");

    // ================= INV-03 / BR-16: firm confirmation needs approval AND covered deposit =================

    [Fact]
    public void ConfirmFirmly_Inv03_WithoutManagementApproval_ThrowsConfirmationPreconditionsNotMet()
    {
        var hallEvent = NewRequested();

        Should.Throw<DomainException>(() => hallEvent.ConfirmFirmly(ApprovalTime))
            .Code.ShouldBe("EVENT.CONFIRMATION_PRECONDITIONS_NOT_MET");

        hallEvent.Status.ShouldBe(HallEventStatus.Requested);
        hallEvent.DequeueDomainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void ConfirmFirmly_Inv03_ApprovedButDepositNotCovered_ThrowsConfirmationPreconditionsNotMet()
    {
        var hallEvent = Approved(); // required 600
        hallEvent.AccreditDeposit(Bob(300m), "TX-1", ApprovalTime.AddHours(1));
        hallEvent.DequeueDomainEvents();

        Should.Throw<DomainException>(() => hallEvent.ConfirmFirmly(ApprovalTime.AddHours(2)))
            .Code.ShouldBe("EVENT.CONFIRMATION_PRECONDITIONS_NOT_MET");

        hallEvent.Status.ShouldBe(HallEventStatus.ApprovedByManagement);
        hallEvent.DequeueDomainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void AccreditDeposit_Inv03_OneCentBelowRequired_DoesNotConfirm()
    {
        var hallEvent = Approved();

        hallEvent.AccreditDeposit(Bob(599.99m), "TX-1", ApprovalTime.AddHours(1));

        hallEvent.Status.ShouldBe(HallEventStatus.ApprovedByManagement);
        hallEvent.Deposit!.IsCovered.ShouldBeFalse();
    }

    [Fact]
    public void AccreditDeposit_Inv03_ExactlyRequired_ConfirmsInTheSameCommand()
    {
        var hallEvent = Approved();

        hallEvent.AccreditDeposit(Bob(600m), "TX-1", ApprovalTime.AddHours(1));

        hallEvent.Status.ShouldBe(HallEventStatus.Confirmed);
        hallEvent.Deposit!.IsCovered.ShouldBeTrue();
    }

    // ================= INV-04 / BR-04: attendance 1..80 =================

    [Theory]
    [InlineData(1)]
    [InlineData(80)]
    public void AttendanceCreate_Inv04_WithinRange_Succeeds(int value) =>
        Attendance.Create(value).Value.ShouldBe(value);

    [Theory]
    [InlineData(0)]
    [InlineData(81)]
    public void AttendanceCreate_Inv04_OutOfRange_ThrowsAttendanceOutOfRange(int value) =>
        Should.Throw<DomainException>(() => Attendance.Create(value)).Code.ShouldBe("EVENT.ATTENDANCE_OUT_OF_RANGE");

    // ================= INV-05 / BR-21: cancelling never refunds, everything accredited becomes credit =================

    [Fact]
    public void Cancel_Inv05_ConfirmedEvent_CreditEqualsEverythingAccredited()
    {
        var hallEvent = Confirmed(); // 600 accredited

        hallEvent.Cancel(Reception, "Cambio de planes", NowUtc.AddHours(5));

        hallEvent.Status.ShouldBe(HallEventStatus.Cancelled);
        hallEvent.CreditGenerated.Amount.ShouldBe(600m);
        var raised = hallEvent.DequeueDomainEvents().OfType<HallEventCancelledWithCredit>().Single();
        raised.CreditAmount.Amount.ShouldBe(600m);
    }

    [Fact]
    public void Cancel_Inv05_ApprovedWithPartialPayment_CreditEqualsPartialPayment()
    {
        var hallEvent = Approved();
        hallEvent.AccreditDeposit(Bob(250m), "TX-1", ApprovalTime.AddHours(1));

        hallEvent.Cancel(Reception, null, NowUtc.AddHours(5));

        hallEvent.CreditGenerated.Amount.ShouldBe(250m);
    }

    [Theory]
    [InlineData(HallEventStatus.Requested)]
    [InlineData(HallEventStatus.ApprovedByManagement)]
    public void Cancel_Inv05_NothingAccredited_CreditIsZeroNeverNegative(HallEventStatus status)
    {
        var hallEvent = EventIn(status);

        hallEvent.Cancel(Reception, "No hace falta", NowUtc.AddHours(5));

        hallEvent.CreditGenerated.Amount.ShouldBe(0m);
        hallEvent.Status.ShouldBe(HallEventStatus.Cancelled);
    }

    [Theory]
    [InlineData(HallEventStatus.InProgress)]
    [InlineData(HallEventStatus.Completed)]
    [InlineData(HallEventStatus.Rejected)]
    [InlineData(HallEventStatus.Cancelled)]
    [InlineData(HallEventStatus.Expired)]
    public void Cancel_Inv05_StatusNotCancellable_ThrowsCancellationNotAllowed(HallEventStatus status)
    {
        var hallEvent = EventIn(status);

        Should.Throw<DomainException>(() => hallEvent.Cancel(Reception, "x", NowUtc.AddHours(5)))
            .Code.ShouldBe("EVENT.CANCELLATION_NOT_ALLOWED");
    }

    [Fact]
    public void Cancel_Inv05_OneSecondBeforeStart_Succeeds()
    {
        var hallEvent = Confirmed();

        hallEvent.Cancel(Reception, "Última hora", hallEvent.Slot.StartUtc.AddSeconds(-1));

        hallEvent.Status.ShouldBe(HallEventStatus.Cancelled);
    }

    [Fact]
    public void Cancel_Inv05_AtStartTime_ThrowsCancellationNotAllowed()
    {
        var hallEvent = Confirmed();

        Should.Throw<DomainException>(() => hallEvent.Cancel(Reception, "Tarde", hallEvent.Slot.StartUtc))
            .Code.ShouldBe("EVENT.CANCELLATION_NOT_ALLOWED");

        hallEvent.Status.ShouldBe(HallEventStatus.Confirmed);
        hallEvent.CreditGenerated.Amount.ShouldBe(0m);
        hallEvent.DequeueDomainEvents().ShouldBeEmpty();
    }

    // ================= INV-07 / BR-12: Management decides within the day of the request =================

    [Fact]
    public void Approve_Inv07_AtLastSecondOfTheDay_Succeeds()
    {
        var hallEvent = NewRequested();

        hallEvent.Approve(Management, 30, DecisionDeadline.AddSeconds(-1)); // 23:59:59 local

        hallEvent.Status.ShouldBe(HallEventStatus.ApprovedByManagement);
    }

    [Fact]
    public void Approve_Inv07_AtMidnightOfTheNextDay_ThrowsDecisionDeadlineExceeded()
    {
        var hallEvent = NewRequested();

        Should.Throw<DomainException>(() => hallEvent.Approve(Management, 30, DecisionDeadline))
            .Code.ShouldBe("EVENT.DECISION_DEADLINE_EXCEEDED");

        hallEvent.Status.ShouldBe(HallEventStatus.Requested);
        hallEvent.Deposit.ShouldBeNull();
        hallEvent.DequeueDomainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void Reject_Inv07_AfterTheDeadline_ThrowsDecisionDeadlineExceeded() =>
        Should.Throw<DomainException>(() =>
                NewRequested().Reject(Management, "Motivo suficientemente largo", DecisionDeadline))
            .Code.ShouldBe("EVENT.DECISION_DEADLINE_EXCEEDED");

    [Fact]
    public void FromAcceptedQuote_Inv07_DeadlineIsMidnightLocalNotUtc()
    {
        // 03:00 UTC on 2 Nov is 23:00 on 1 Nov in La Paz: the deadline is 00:00 local of 2 Nov = 04:00 UTC.
        var requestedAt = new DateTime(2026, 11, 2, 3, 0, 0, DateTimeKind.Utc);

        var hallEvent = NewRequested(nowUtc: requestedAt);

        hallEvent.DecisionDeadlineUtc.ShouldBe(DecisionDeadline);
    }

    // ================= INV-08 / BR-13: rejection needs a reason of at least 10 characters =================

    [Fact]
    public void Reject_Inv08_ReasonOfExactlyTenCharacters_Succeeds()
    {
        var hallEvent = NewRequested();

        hallEvent.Reject(Management, "0123456789", ApprovalTime);

        hallEvent.Status.ShouldBe(HallEventStatus.Rejected);
        hallEvent.Decision!.RejectionReason.ShouldBe("0123456789");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("         ")]
    [InlineData("012345678")]       // 9 characters
    [InlineData("   corto   ")]     // padding does not count
    public void Reject_Inv08_ReasonTooShort_ThrowsRejectionReasonRequired(string? reason)
    {
        var hallEvent = NewRequested();

        Should.Throw<DomainException>(() => hallEvent.Reject(Management, reason, ApprovalTime))
            .Code.ShouldBe("EVENT.REJECTION_REASON_REQUIRED");

        hallEvent.Status.ShouldBe(HallEventStatus.Requested);
        hallEvent.Decision.ShouldBeNull();
        hallEvent.History.Count.ShouldBe(1);
        hallEvent.DequeueDomainEvents().ShouldBeEmpty();
    }

    // ================= INV-09 / BR-15: deposit only when approved; a transaction is accredited once =================

    [Theory]
    [InlineData(HallEventStatus.Requested)]
    [InlineData(HallEventStatus.Confirmed)]
    [InlineData(HallEventStatus.InProgress)]
    [InlineData(HallEventStatus.Completed)]
    [InlineData(HallEventStatus.Rejected)]
    [InlineData(HallEventStatus.Cancelled)]
    [InlineData(HallEventStatus.Expired)]
    public void AccreditDeposit_Inv09_StatusOtherThanApproved_ThrowsDepositNotAccepted(HallEventStatus status)
    {
        var hallEvent = EventIn(status);

        Should.Throw<DomainException>(() => hallEvent.AccreditDeposit(Bob(100m), "TX-NEW", ApprovalTime.AddHours(2)))
            .Code.ShouldBe("EVENT.DEPOSIT_NOT_ACCEPTED");

        hallEvent.Status.ShouldBe(status);
        hallEvent.DequeueDomainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void AccreditDeposit_Inv09_SameTransactionTwice_IsIdempotentNotAnError()
    {
        var hallEvent = Approved();
        hallEvent.AccreditDeposit(Bob(200m), "TX-1", ApprovalTime.AddHours(1));
        hallEvent.DequeueDomainEvents();

        var excess = hallEvent.AccreditDeposit(Bob(200m), "TX-1", ApprovalTime.AddHours(2));

        excess.Amount.ShouldBe(0m);
        hallEvent.Deposit!.Accredited.Amount.ShouldBe(200m); // not 400
        hallEvent.DequeueDomainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void AccreditDeposit_Inv09_RepeatedTransactionAfterConfirmation_IsStillIdempotent()
    {
        var hallEvent = Approved();
        hallEvent.AccreditDeposit(Bob(600m), "TX-1", ApprovalTime.AddHours(1));
        hallEvent.DequeueDomainEvents();

        Should.NotThrow(() => hallEvent.AccreditDeposit(Bob(600m), "TX-1", ApprovalTime.AddHours(2)));

        hallEvent.Status.ShouldBe(HallEventStatus.Confirmed);
        hallEvent.Deposit!.Accredited.Amount.ShouldBe(600m);
        hallEvent.DequeueDomainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void AccreditDeposit_Inv09_JustBeforeDepositDeadline_Succeeds()
    {
        var hallEvent = Approved();
        var deadline = hallEvent.DepositDeadlineUtc!.Value;

        deadline.ShouldBe(ApprovalTime.AddHours(48));
        Should.NotThrow(() => hallEvent.AccreditDeposit(Bob(100m), "TX-1", deadline.AddSeconds(-1)));
    }

    [Fact]
    public void AccreditDeposit_Inv09_AtDepositDeadline_ThrowsDepositNotAccepted()
    {
        var hallEvent = Approved();

        Should.Throw<DomainException>(() =>
                hallEvent.AccreditDeposit(Bob(100m), "TX-1", hallEvent.DepositDeadlineUtc!.Value))
            .Code.ShouldBe("EVENT.DEPOSIT_NOT_ACCEPTED");

        hallEvent.Deposit!.Accredited.Amount.ShouldBe(0m);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void AccreditDeposit_Inv09_BlankTransactionId_ThrowsDepositNotAccepted(string? transactionId) =>
        Should.Throw<DomainException>(() => Approved().AccreditDeposit(Bob(100m), transactionId!, ApprovalTime.AddHours(1)))
            .Code.ShouldBe("EVENT.DEPOSIT_NOT_ACCEPTED");

    // ================= INV-10: accredited amount never negative nor in another currency =================

    [Fact]
    public void AccreditDeposit_Inv10_DifferentCurrency_ThrowsCurrencyMismatchAndKeepsState()
    {
        var hallEvent = Approved();

        Should.Throw<DomainException>(() =>
                hallEvent.AccreditDeposit(Money.Of(100m, "USD"), "TX-USD", ApprovalTime.AddHours(1)))
            .Code.ShouldBe("MONEY.CURRENCY_MISMATCH");

        hallEvent.Deposit!.Accredited.Amount.ShouldBe(0m);
        hallEvent.Deposit.TransactionIds.ShouldBeEmpty();
        hallEvent.DequeueDomainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void MoneyOf_Inv10_NegativeAmount_ThrowsNegativeAmount() =>
        Should.Throw<DomainException>(() => Money.Of(-0.01m)).Code.ShouldBe("MONEY.NEGATIVE_AMOUNT");

    [Fact]
    public void AccreditDeposit_Inv10_ZeroPayment_ThrowsDepositNotAccepted() =>
        Should.Throw<DomainException>(() => Approved().AccreditDeposit(Bob(0m), "TX-0", ApprovalTime.AddHours(1)))
            .Code.ShouldBe("EVENT.DEPOSIT_NOT_ACCEPTED");

    // ================= INV-11 / BR-18: start from (start - 1 h) and before the end =================

    [Fact]
    public void Start_Inv11_ExactlyOneHourBeforeStart_Succeeds()
    {
        var hallEvent = Confirmed();

        hallEvent.Start(hallEvent.Slot.StartUtc.AddHours(-1), Operations);

        hallEvent.Status.ShouldBe(HallEventStatus.InProgress);
    }

    [Fact]
    public void Start_Inv11_OneSecondBeforeTheWindow_ThrowsStartOutsideWindow()
    {
        var hallEvent = Confirmed();

        Should.Throw<DomainException>(() =>
                hallEvent.Start(hallEvent.Slot.StartUtc.AddHours(-1).AddSeconds(-1), Operations))
            .Code.ShouldBe("EVENT.START_OUTSIDE_WINDOW");

        hallEvent.Status.ShouldBe(HallEventStatus.Confirmed);
        hallEvent.DequeueDomainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void Start_Inv11_OneSecondBeforeTheEnd_Succeeds()
    {
        var hallEvent = Confirmed();

        hallEvent.Start(hallEvent.Slot.EndUtc.AddSeconds(-1), Operations);

        hallEvent.Status.ShouldBe(HallEventStatus.InProgress);
    }

    [Fact]
    public void Start_Inv11_AtTheEnd_ThrowsStartOutsideWindow()
    {
        var hallEvent = Confirmed();

        Should.Throw<DomainException>(() => hallEvent.Start(hallEvent.Slot.EndUtc, Operations))
            .Code.ShouldBe("EVENT.START_OUTSIDE_WINDOW");
    }

    // ================= INV-12 / BR-23: reschedule only cancelled events with credit, keeping their content =================

    [Fact]
    public void Reschedule_Inv12_CancelledWithCredit_KeepsDurationAttendanceServicesAndTotal()
    {
        var hallEvent = Confirmed(extras: [Equipment("PROJECTOR", "Proyector", 150m)]);
        hallEvent.Cancel(Reception, "Cambio de fecha", NowUtc.AddHours(5));
        var extras = hallEvent.ExtraServices.ToList();

        hallEvent.Reschedule(Slot(10, 14, Today.AddDays(20)), Bob(600m), Reception, NowUtc.AddHours(6), Today);

        hallEvent.Slot.DurationHours.ShouldBe(4);
        hallEvent.Attendance.Value.ShouldBe(40);
        hallEvent.Total.ShouldBe(Total);
        hallEvent.ExtraServices.ShouldBe(extras);
        hallEvent.RescheduleCount.ShouldBe(1);
    }

    [Theory]
    [InlineData(HallEventStatus.Requested)]
    [InlineData(HallEventStatus.ApprovedByManagement)]
    [InlineData(HallEventStatus.Confirmed)]
    [InlineData(HallEventStatus.InProgress)]
    [InlineData(HallEventStatus.Completed)]
    [InlineData(HallEventStatus.Rejected)]
    [InlineData(HallEventStatus.Expired)]
    public void Reschedule_Inv12_StatusOtherThanCancelled_ThrowsRescheduleNotAllowed(HallEventStatus status)
    {
        var hallEvent = EventIn(status);

        Should.Throw<DomainException>(() =>
                hallEvent.Reschedule(Slot(10, 14, Today.AddDays(20)), Bob(600m), Reception, NowUtc.AddHours(6), Today))
            .Code.ShouldBe("EVENT.RESCHEDULE_NOT_ALLOWED");

        hallEvent.Status.ShouldBe(status);
        hallEvent.DequeueDomainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void Reschedule_Inv12_CancelledWithoutCredit_ThrowsRescheduleNotAllowed()
    {
        var hallEvent = Approved(); // nothing paid
        hallEvent.Cancel(Reception, null, NowUtc.AddHours(5));
        hallEvent.CreditGenerated.Amount.ShouldBe(0m);

        Should.Throw<DomainException>(() =>
                hallEvent.Reschedule(Slot(10, 14, Today.AddDays(20)), Bob(100m), Reception, NowUtc.AddHours(6), Today))
            .Code.ShouldBe("EVENT.RESCHEDULE_NOT_ALLOWED");

        hallEvent.Status.ShouldBe(HallEventStatus.Cancelled);
    }

    [Fact]
    public void Reschedule_Inv12_DifferentDuration_ThrowsRescheduleNotAllowed()
    {
        var hallEvent = EventIn(HallEventStatus.Cancelled); // 4 h

        Should.Throw<DomainException>(() =>
                hallEvent.Reschedule(Slot(10, 15, Today.AddDays(20)), Bob(600m), Reception, NowUtc.AddHours(6), Today))
            .Code.ShouldBe("EVENT.RESCHEDULE_NOT_ALLOWED");

        hallEvent.Slot.Date.ShouldBe(EventDay);
        hallEvent.RescheduleCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(600.01)] // one cent above the required deposit
    public void Reschedule_Inv12_CreditAppliedOutOfRange_ThrowsRescheduleNotAllowed(double credit)
    {
        var hallEvent = EventIn(HallEventStatus.Cancelled);

        Should.Throw<DomainException>(() => hallEvent.Reschedule(
                Slot(10, 14, Today.AddDays(20)), Bob((decimal)credit), Reception, NowUtc.AddHours(6), Today))
            .Code.ShouldBe("EVENT.RESCHEDULE_NOT_ALLOWED");

        hallEvent.Status.ShouldBe(HallEventStatus.Cancelled);
    }

    [Theory]
    [InlineData(3)]    // today + 3 days: lower bound
    [InlineData(180)]  // upper bound
    public void Reschedule_Br05_DateWithinWindow_Succeeds(int daysAhead)
    {
        var hallEvent = EventIn(HallEventStatus.Cancelled);

        hallEvent.Reschedule(Slot(10, 14, Today.AddDays(daysAhead)), Bob(600m), Reception, NowUtc.AddHours(6), Today);

        hallEvent.Status.ShouldBe(HallEventStatus.Confirmed);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(181)]
    public void Reschedule_Br05_DateOutsideWindow_ThrowsDateOutOfRange(int daysAhead)
    {
        var hallEvent = EventIn(HallEventStatus.Cancelled);

        Should.Throw<DomainException>(() => hallEvent.Reschedule(
                Slot(10, 14, Today.AddDays(daysAhead)), Bob(600m), Reception, NowUtc.AddHours(6), Today))
            .Code.ShouldBe("QUOTE.DATE_OUT_OF_RANGE");

        hallEvent.Status.ShouldBe(HallEventStatus.Cancelled);
    }

    [Theory]
    [InlineData(7, 11)]
    [InlineData(19, 23)]
    public void Reschedule_Br02_OutsideOperatingHours_ThrowsSlotOutsideOperatingHours(int start, int end)
    {
        var hallEvent = EventIn(HallEventStatus.Cancelled);

        Should.Throw<DomainException>(() => hallEvent.Reschedule(
                Slot(start, end, Today.AddDays(20)), Bob(600m), Reception, NowUtc.AddHours(6), Today))
            .Code.ShouldBe("QUOTE.SLOT_OUTSIDE_OPERATING_HOURS");
    }

    [Fact]
    public void EnsureCanReschedule_Inv12_CancelledWithCredit_PassesAndExposesTheDeposit()
    {
        var hallEvent = EventIn(HallEventStatus.Cancelled);

        hallEvent.EnsureCanReschedule(Slot(10, 14, Today.AddDays(20)), Today);

        hallEvent.Deposit.ShouldNotBeNull();
        hallEvent.Status.ShouldBe(HallEventStatus.Cancelled); // checks only, no mutation
        hallEvent.DequeueDomainEvents().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(HallEventStatus.Confirmed, "EVENT.RESCHEDULE_NOT_ALLOWED")]
    [InlineData(HallEventStatus.Requested, "EVENT.RESCHEDULE_NOT_ALLOWED")]
    public void EnsureCanReschedule_Inv12_NotCancelled_ThrowsRescheduleNotAllowed(HallEventStatus status, string code) =>
        Should.Throw<DomainException>(() => EventIn(status).EnsureCanReschedule(Slot(10, 14, Today.AddDays(20)), Today))
            .Code.ShouldBe(code);

    [Fact]
    public void EnsureCanReschedule_Br05_DateOutsideWindow_ThrowsDateOutOfRange() =>
        Should.Throw<DomainException>(() =>
                EventIn(HallEventStatus.Cancelled).EnsureCanReschedule(Slot(10, 14, Today.AddDays(2)), Today))
            .Code.ShouldBe("QUOTE.DATE_OUT_OF_RANGE");

    [Theory]
    [InlineData(8, 12)]
    [InlineData(19, 23)]
    public void SlotMovedTo_Br23_KeepsTheDurationAndMovesDayAndStart(int start, int expectedEnd)
    {
        var hallEvent = NewRequested(); // 09-13: 4 h
        var day = Today.AddDays(30);

        var moved = hallEvent.SlotMovedTo(day, new TimeOnly(start, 0));

        moved.Date.ShouldBe(day);
        moved.Start.ShouldBe(new TimeOnly(start, 0));
        moved.End.ShouldBe(new TimeOnly(expectedEnd, 0));
        moved.DurationHours.ShouldBe(hallEvent.Slot.DurationHours);
    }

    [Fact]
    public void SlotMovedTo_Br23_EndWouldPassMidnight_ThrowsSlotOutsideOperatingHours() =>
        Should.Throw<DomainException>(() => NewRequested().SlotMovedTo(Today.AddDays(30), new TimeOnly(20, 0)))
            .Code.ShouldBe("QUOTE.SLOT_OUTSIDE_OPERATING_HOURS");

    // ================= INV-13 / BR-08: extra services only through the root; equipment <= stock =================

    [Fact]
    public void FromAcceptedQuote_Inv13_EquipmentUpToStock_CreatesTheItems()
    {
        var hallEvent = NewRequested(extras:
            [Equipment("TV", "Pantalla TV", 100m, 2), Catering("COFFEE_BREAK", "Coffee break", 35m, 40)]);

        hallEvent.ExtraServices.Count.ShouldBe(2);
        hallEvent.ExtraServices.Sum(e => e.LineTotal.Amount).ShouldBe(1600m);
    }

    [Fact]
    public void FromAcceptedQuote_Inv13_EquipmentAboveStock_ThrowsExtraServiceInvalid() =>
        Should.Throw<DomainException>(() => NewRequested(extras: [Equipment("PROJECTOR", "Proyector", 150m, 2)]))
            .Code.ShouldBe("EVENT.EXTRA_SERVICE_INVALID");

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void FromAcceptedQuote_Inv13_QuantityBelowOne_ThrowsExtraServiceInvalid(int quantity) =>
        Should.Throw<DomainException>(() => NewRequested(extras: [Equipment("TV", "Pantalla TV", 100m, quantity)]))
            .Code.ShouldBe("EVENT.EXTRA_SERVICE_INVALID");

    [Fact]
    public void FromAcceptedQuote_Inv13_RepeatedCode_ThrowsExtraServiceInvalid() =>
        Should.Throw<DomainException>(() => NewRequested(extras:
            [Equipment("TV", "Pantalla TV", 100m), Equipment("TV", "Pantalla TV", 100m)]))
            .Code.ShouldBe("EVENT.EXTRA_SERVICE_INVALID");

    [Fact]
    public void ExtraServices_Inv13_CannotBeModifiedFromOutsideTheRoot()
    {
        var hallEvent = NewRequested(extras: [Equipment("TV", "Pantalla TV", 100m)]);

        (hallEvent.ExtraServices is List<ExtraServiceItem>).ShouldBeFalse();
        Should.Throw<NotSupportedException>(() => ((IList<ExtraServiceItem>)hallEvent.ExtraServices).Clear());
        typeof(ExtraServiceItem).GetConstructors().ShouldBeEmpty(); // no public constructor
    }

    // ================= Request creation (BR-02, BR-05 re-checked) =================

    [Theory]
    [InlineData(2)]
    [InlineData(181)]
    public void FromAcceptedQuote_Br05_DateOutsideWindow_ThrowsDateOutOfRange(int daysAhead) =>
        Should.Throw<DomainException>(() => NewRequested(slot: Slot(9, 13, Today.AddDays(daysAhead))))
            .Code.ShouldBe("QUOTE.DATE_OUT_OF_RANGE");

    [Fact]
    public void FromAcceptedQuote_Br02_OutsideOperatingHours_ThrowsSlotOutsideOperatingHours() =>
        Should.Throw<DomainException>(() => NewRequested(slot: Slot(7, 11)))
            .Code.ShouldBe("QUOTE.SLOT_OUTSIDE_OPERATING_HOURS");
}
