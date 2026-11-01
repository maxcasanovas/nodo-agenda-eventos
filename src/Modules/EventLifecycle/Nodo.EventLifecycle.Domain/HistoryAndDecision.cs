using Nodo.SharedKernel;

namespace Nodo.EventLifecycle.Domain;

public enum HallEventStatus
{
    Requested,
    ApprovedByManagement,
    Confirmed,
    InProgress,
    Completed,
    Rejected,
    Cancelled,
    Expired
}

/// <summary>One line of the event's timeline: status reached, when, who and a note (business-rules §4).</summary>
public sealed class HistoryEntry : ValueObject
{
    private HistoryEntry(HallEventStatus status, DateTime occurredAtUtc, string actor, string note)
    {
        Status = status;
        OccurredAtUtc = occurredAtUtc;
        Actor = actor;
        Note = note;
    }

    public HallEventStatus Status { get; }

    public DateTime OccurredAtUtc { get; }

    public string Actor { get; }

    public string Note { get; }

    public static HistoryEntry Create(HallEventStatus status, DateTime occurredAtUtc, string actor, string note)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        return new HistoryEntry(status, occurredAtUtc, actor.Trim(), note?.Trim() ?? string.Empty);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Status;
        yield return OccurredAtUtc;
        yield return Actor;
        yield return Note;
    }
}

/// <summary>Management's decision on a request (BR-12, BR-13).</summary>
public sealed class ManagementDecision : ValueObject
{
    private ManagementDecision(string decidedBy, DateTime decidedAtUtc, bool approved, string? rejectionReason)
    {
        DecidedBy = decidedBy;
        DecidedAtUtc = decidedAtUtc;
        Approved = approved;
        RejectionReason = rejectionReason;
    }

    public string DecidedBy { get; }

    public DateTime DecidedAtUtc { get; }

    public bool Approved { get; }

    public string? RejectionReason { get; }

    internal static ManagementDecision Approve(string by, DateTime nowUtc) => new(by, nowUtc, true, null);

    internal static ManagementDecision Reject(string by, string reason, DateTime nowUtc) => new(by, nowUtc, false, reason);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return DecidedBy;
        yield return DecidedAtUtc;
        yield return Approved;
        yield return RejectionReason;
    }
}
