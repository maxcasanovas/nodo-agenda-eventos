using Nodo.EventLifecycle.Domain;
using Nodo.SharedKernel;

namespace Nodo.EventLifecycle.UnitTests;

internal static class TestData
{
    public const string Management = "Lucía Paz";
    public const string Reception = "Recepción Nodo";
    public const string Operations = "Host de turno";

    // 12:00 UTC = 08:00 in La Paz (UTC-4), 1 Nov 2026.
    public static readonly DateTime NowUtc = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    public static readonly DateOnly Today = new(2026, 11, 1);
    public static readonly DateOnly EventDay = Today.AddDays(10);

    /// <summary>Start of the next local day = 04:00 UTC on 2 Nov (decision deadline for a request made at <see cref="NowUtc"/>).</summary>
    public static readonly DateTime DecisionDeadline = new(2026, 11, 2, 4, 0, 0, DateTimeKind.Utc);

    public static readonly CustomerId Customer = new(Guid.Parse("22222222-0000-0000-0000-000000000002"));
    public static readonly HallId Hall = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    public static readonly QuoteId Quote = new(Guid.Parse("33333333-0000-0000-0000-000000000003"));

    /// <summary>With a 30 % deposit: required 600 Bs.</summary>
    public static readonly Money Total = Money.Of(2000m);

    public static readonly IReadOnlyDictionary<string, int> Stock = new Dictionary<string, int>
    {
        ["PROJECTOR"] = 1, ["TV"] = 2, ["SOUND"] = 1, ["LIGHTS"] = 1
    };

    public static Money Bob(decimal amount) => Money.Of(amount);

    public static TimeSlot Slot(int startHour, int endHour, DateOnly? date = null) =>
        TimeSlot.Create(date ?? EventDay, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0));

    public static ExtraServiceSnapshot Equipment(string code, string name, decimal unitCost, int quantity = 1) =>
        new(ExtraServiceType.OwnEquipment, code, name, quantity, Money.Of(unitCost));

    public static ExtraServiceSnapshot Catering(string code, string name, decimal perPerson, int people) =>
        new(ExtraServiceType.ExternalCatering, code, name, people, Money.Of(perPerson));

    public static AcceptedQuoteSnapshot Snapshot(
        TimeSlot? slot = null, int attendance = 40, IReadOnlyCollection<ExtraServiceSnapshot>? extras = null) =>
        new(Quote, Customer, Hall, slot ?? Slot(9, 13), Attendance.Create(attendance), Total, extras ?? []);

    public static HallEvent NewRequested(
        TimeSlot? slot = null, int attendance = 40, IReadOnlyCollection<ExtraServiceSnapshot>? extras = null,
        DateTime? nowUtc = null)
    {
        var hallEvent = HallEventFactory.FromAcceptedQuote(
            Snapshot(slot, attendance, extras), nowUtc ?? NowUtc, Today, Stock);
        hallEvent.DequeueDomainEvents();
        return hallEvent;
    }

    public static DateTime ApprovalTime => NowUtc.AddHours(1);

    public static HallEvent Approved(int percentage = 30, IReadOnlyCollection<ExtraServiceSnapshot>? extras = null)
    {
        var hallEvent = NewRequested(extras: extras);
        hallEvent.Approve(Management, percentage, ApprovalTime);
        hallEvent.DequeueDomainEvents();
        return hallEvent;
    }

    public static HallEvent Confirmed(int percentage = 30, IReadOnlyCollection<ExtraServiceSnapshot>? extras = null)
    {
        var hallEvent = Approved(percentage, extras);
        hallEvent.AccreditDeposit(hallEvent.Deposit!.Required, "TX-FULL", ApprovalTime.AddHours(1));
        hallEvent.DequeueDomainEvents();
        return hallEvent;
    }

    /// <summary>Builds an event that has reached <paramref name="status"/> using only public commands.</summary>
    public static HallEvent EventIn(HallEventStatus status)
    {
        HallEvent hallEvent;
        switch (status)
        {
            case HallEventStatus.Requested:
                hallEvent = NewRequested();
                break;
            case HallEventStatus.ApprovedByManagement:
                hallEvent = Approved();
                break;
            case HallEventStatus.Confirmed:
                hallEvent = Confirmed();
                break;
            case HallEventStatus.InProgress:
                hallEvent = Confirmed();
                hallEvent.Start(hallEvent.Slot.StartUtc.AddMinutes(-30), Operations);
                break;
            case HallEventStatus.Completed:
                hallEvent = Confirmed();
                hallEvent.Start(hallEvent.Slot.StartUtc.AddMinutes(-30), Operations);
                hallEvent.Complete(hallEvent.Slot.EndUtc.AddMinutes(-10), Operations);
                break;
            case HallEventStatus.Rejected:
                hallEvent = NewRequested();
                hallEvent.Reject(Management, "El salón está reservado para mantenimiento.", ApprovalTime);
                break;
            case HallEventStatus.Cancelled:
                hallEvent = Confirmed();
                hallEvent.Cancel(Reception, "El cliente ya no lo necesita.", NowUtc.AddHours(5));
                break;
            case HallEventStatus.Expired:
                hallEvent = NewRequested();
                hallEvent.Expire(DecisionDeadline);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }

        hallEvent.DequeueDomainEvents();
        return hallEvent;
    }
}
