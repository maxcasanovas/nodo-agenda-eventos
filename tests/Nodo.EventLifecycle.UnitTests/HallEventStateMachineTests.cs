using Nodo.EventLifecycle.Domain;
using Nodo.SharedKernel;
using static Nodo.EventLifecycle.UnitTests.TestData;

namespace Nodo.EventLifecycle.UnitTests;

/// <summary>
/// INV-06: only the transitions of the state machine (business-rules.md §4) are possible.
/// Every status x command pair outside the allowed set must fail with the catalog code and change nothing.
/// </summary>
public class HallEventStateMachineTests
{
    private sealed record Command(
        string Name,
        Action<HallEvent> Run,
        HallEventStatus[] Allowed,
        string DisallowedCode);

    // Commands with a dedicated catalog code use it (INV-03, INV-05, INV-09, INV-12); the rest use INV-06.
    private static readonly Command[] Commands =
    [
        new("Approve", e => e.Approve(Management, 30, ApprovalTime),
            [HallEventStatus.Requested], "EVENT.INVALID_STATE_TRANSITION"),
        new("Reject", e => e.Reject(Management, "Motivo suficientemente largo", ApprovalTime),
            [HallEventStatus.Requested], "EVENT.INVALID_STATE_TRANSITION"),
        new("AccreditDeposit", e => e.AccreditDeposit(Bob(100m), "TX-MATRIX", ApprovalTime.AddHours(2)),
            [HallEventStatus.ApprovedByManagement], "EVENT.DEPOSIT_NOT_ACCEPTED"),
        new("ConfirmFirmly", e => e.ConfirmFirmly(ApprovalTime.AddHours(2)),
            [], "EVENT.INVALID_STATE_TRANSITION"), // see ConfirmFirmly note below
        new("AssignExtraServices", e => e.AssignExtraServices(NowUtc.AddHours(3)),
            [HallEventStatus.Confirmed], "EVENT.INVALID_STATE_TRANSITION"),
        new("Start", e => e.Start(e.Slot.StartUtc, Operations),
            [HallEventStatus.Confirmed], "EVENT.INVALID_STATE_TRANSITION"),
        new("Complete", e => e.Complete(e.Slot.EndUtc, Operations),
            [HallEventStatus.InProgress], "EVENT.INVALID_STATE_TRANSITION"),
        new("Cancel", e => e.Cancel(Reception, "Cambio", NowUtc.AddHours(5)),
            [HallEventStatus.Requested, HallEventStatus.ApprovedByManagement, HallEventStatus.Confirmed],
            "EVENT.CANCELLATION_NOT_ALLOWED"),
        new("Expire", e => e.Expire(NowUtc.AddDays(30)),
            [HallEventStatus.Requested, HallEventStatus.ApprovedByManagement, HallEventStatus.Expired],
            "EVENT.INVALID_STATE_TRANSITION"),
        new("Reschedule", e => e.Reschedule(Slot(10, 14, Today.AddDays(20)), Bob(600m), Reception, NowUtc.AddHours(6), Today),
            [HallEventStatus.Cancelled], "EVENT.RESCHEDULE_NOT_ALLOWED")
    ];

    public static TheoryData<HallEventStatus, string> DisallowedPairs()
    {
        var data = new TheoryData<HallEventStatus, string>();
        foreach (var status in Enum.GetValues<HallEventStatus>())
        {
            foreach (var command in Commands)
            {
                // ConfirmFirmly is covered by dedicated tests below because its code depends on the status.
                if (command.Name != "ConfirmFirmly" && !command.Allowed.Contains(status))
                    data.Add(status, command.Name);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DisallowedPairs))]
    public void Command_Inv06_StatusNotAllowed_ThrowsCatalogCodeAndChangesNothing(HallEventStatus status, string commandName)
    {
        var command = Commands.Single(c => c.Name == commandName);
        var hallEvent = EventIn(status);
        var historyBefore = hallEvent.History.Count;
        var depositBefore = hallEvent.Deposit;
        var creditBefore = hallEvent.CreditGenerated;
        var slotBefore = hallEvent.Slot;

        var ex = Should.Throw<DomainException>(() => command.Run(hallEvent));

        ex.Code.ShouldBe(command.DisallowedCode);
        hallEvent.Status.ShouldBe(status);
        hallEvent.History.Count.ShouldBe(historyBefore);
        hallEvent.Deposit.ShouldBe(depositBefore);
        hallEvent.CreditGenerated.ShouldBe(creditBefore);
        hallEvent.Slot.ShouldBe(slotBefore);
        hallEvent.DequeueDomainEvents().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(HallEventStatus.Confirmed)]
    [InlineData(HallEventStatus.InProgress)]
    [InlineData(HallEventStatus.Completed)]
    [InlineData(HallEventStatus.Rejected)]
    [InlineData(HallEventStatus.Cancelled)]
    [InlineData(HallEventStatus.Expired)]
    public void ConfirmFirmly_Inv06_StatusPastApproval_ThrowsInvalidStateTransition(HallEventStatus status)
    {
        var hallEvent = EventIn(status);

        Should.Throw<DomainException>(() => hallEvent.ConfirmFirmly(ApprovalTime.AddHours(2)))
            .Code.ShouldBe("EVENT.INVALID_STATE_TRANSITION");

        hallEvent.Status.ShouldBe(status);
        hallEvent.DequeueDomainEvents().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(HallEventStatus.Requested)]
    [InlineData(HallEventStatus.ApprovedByManagement)]
    public void ConfirmFirmly_Inv03_WithoutApprovalOrCoveredDeposit_ThrowsConfirmationPreconditionsNotMet(HallEventStatus status)
    {
        var hallEvent = EventIn(status);

        Should.Throw<DomainException>(() => hallEvent.ConfirmFirmly(ApprovalTime.AddHours(2)))
            .Code.ShouldBe("EVENT.CONFIRMATION_PRECONDITIONS_NOT_MET");
    }

    // ---- The allowed transitions of the diagram ----

    public static TheoryData<HallEventStatus, string, HallEventStatus> AllowedTransitions() => new()
    {
        { HallEventStatus.Requested, "Approve", HallEventStatus.ApprovedByManagement },
        { HallEventStatus.Requested, "Reject", HallEventStatus.Rejected },
        { HallEventStatus.Requested, "Cancel", HallEventStatus.Cancelled },
        { HallEventStatus.ApprovedByManagement, "Cancel", HallEventStatus.Cancelled },
        { HallEventStatus.Confirmed, "Start", HallEventStatus.InProgress },
        { HallEventStatus.Confirmed, "Cancel", HallEventStatus.Cancelled },
        { HallEventStatus.InProgress, "Complete", HallEventStatus.Completed },
        { HallEventStatus.Cancelled, "Reschedule", HallEventStatus.Confirmed }
    };

    [Theory]
    [MemberData(nameof(AllowedTransitions))]
    public void Command_Inv06_AllowedTransition_MovesToTheExpectedStatus(
        HallEventStatus from, string commandName, HallEventStatus to)
    {
        var hallEvent = EventIn(from);

        Commands.Single(c => c.Name == commandName).Run(hallEvent);

        hallEvent.Status.ShouldBe(to);
        hallEvent.History.Last().Status.ShouldBe(to);
    }

    [Fact]
    public void Transitions_Inv06_ApprovedToConfirmedHappensThroughDepositAndApprovedToExpiredThroughDeadline()
    {
        var paid = EventIn(HallEventStatus.ApprovedByManagement);
        paid.AccreditDeposit(Bob(600m), "TX-OK", ApprovalTime.AddHours(2));
        paid.Status.ShouldBe(HallEventStatus.Confirmed);

        var unpaid = EventIn(HallEventStatus.ApprovedByManagement);
        unpaid.Expire(unpaid.DepositDeadlineUtc!.Value);
        unpaid.Status.ShouldBe(HallEventStatus.Expired);
    }

    [Theory]
    [InlineData(HallEventStatus.Rejected)]
    [InlineData(HallEventStatus.Completed)]
    [InlineData(HallEventStatus.Expired)]
    public void FinalStatus_Inv06_AcceptsNoCommandThatChangesIt(HallEventStatus status)
    {
        var hallEvent = EventIn(status);

        foreach (var command in Commands.Where(c => c.Name is not ("ConfirmFirmly" or "Expire")))
        {
            Should.Throw<DomainException>(() => command.Run(hallEvent));
        }

        hallEvent.Status.ShouldBe(status);
    }
}
