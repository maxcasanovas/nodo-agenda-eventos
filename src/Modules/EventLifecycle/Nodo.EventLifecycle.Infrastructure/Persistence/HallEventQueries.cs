using Microsoft.EntityFrameworkCore;
using Nodo.EventLifecycle.Application;
using Nodo.EventLifecycle.Contracts;
using Nodo.EventLifecycle.Domain;

namespace Nodo.EventLifecycle.Infrastructure.Persistence;

/// <summary>Read side (AsNoTracking). Handlers add the customer name.</summary>
internal sealed class HallEventQueries(EventLifecycleDbContext db) : IHallEventQueries
{
    private static readonly HallEventStatus[] InOperation = [HallEventStatus.Confirmed, HallEventStatus.InProgress];

    public async Task<HallEventDto?> GetAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        var id = new HallEventId(eventId);
        var hallEvent = await db.HallEvents.AsNoTracking()
            .Include(e => e.ExtraServices)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        return hallEvent is null ? null : ToDto(hallEvent);
    }

    public async Task<IReadOnlyCollection<HallEventListItemDto>> ListAsync(
        string? status, Guid? customerId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var events = db.HallEvents.AsNoTracking();

        if (status is not null)
        {
            var parsed = Enum.Parse<HallEventStatus>(status, ignoreCase: true);
            events = events.Where(e => e.Status == parsed);
        }

        if (customerId is { } customer)
        {
            var id = new CustomerId(customer);
            events = events.Where(e => e.CustomerId == id);
        }

        if (from is { } start)
            events = events.Where(e => e.Slot.Date >= start);
        if (to is { } end)
            events = events.Where(e => e.Slot.Date <= end);

        return await ListItemsAsync(events.OrderBy(e => e.Slot.Date).ThenBy(e => e.Slot.Start), cancellationToken);
    }

    public async Task<IReadOnlyCollection<HallEventListItemDto>> GetPendingApprovalsAsync(
        CancellationToken cancellationToken = default) =>
        await ListItemsAsync(
            db.HallEvents.AsNoTracking()
                .Where(e => e.Status == HallEventStatus.Requested)
                .OrderBy(e => e.DecisionDeadlineUtc),
            cancellationToken);

    public async Task<IReadOnlyCollection<HallEventListItemDto>> GetTodayOperationsAsync(
        DateOnly todayLocal, CancellationToken cancellationToken = default) =>
        await ListItemsAsync(
            db.HallEvents.AsNoTracking()
                .Where(e => InOperation.Contains(e.Status) && e.Slot.Date == todayLocal)
                .OrderBy(e => e.Slot.Start),
            cancellationToken);

    public async Task<CustomerCreditDto?> GetCreditAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var id = new CustomerId(customerId);
        var credit = await db.CustomerCredits.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (credit is null)
            return null;

        return new CustomerCreditDto(
            credit.Id.Value, null, credit.Balance.Amount,
            credit.Movements
                .OrderByDescending(m => m.OccurredAtUtc)
                .Select(m => new CreditMovementDto(m.Type.ToString(), m.Amount.Amount, m.EventId.Value, m.Reason, m.OccurredAtUtc))
                .ToList());
    }

    public async Task<IReadOnlyCollection<EventSummary>> GetSummariesAsync(
        IReadOnlyCollection<Guid> eventIds, CancellationToken cancellationToken = default)
    {
        var ids = eventIds.Select(i => new HallEventId(i)).ToList();
        var rows = await db.HallEvents.AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, e.CustomerId, e.Status })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new EventSummary(r.Id.Value, r.CustomerId.Value, null, r.Status.ToString())).ToList();
    }

    private static async Task<IReadOnlyCollection<HallEventListItemDto>> ListItemsAsync(
        IQueryable<HallEvent> events, CancellationToken cancellationToken)
    {
        var rows = await events.Select(e => new
        {
            e.Id,
            e.CustomerId,
            e.Slot.Date,
            e.Slot.Start,
            e.Slot.End,
            e.Attendance,
            Total = e.Total.Amount,
            e.Status,
            e.DecisionDeadlineUtc,
            e.DepositDeadlineUtc,
            Required = e.Deposit == null ? 0m : e.Deposit.Required.Amount,
            Accredited = e.Deposit == null ? 0m : e.Deposit.Accredited.Amount
        }).ToListAsync(cancellationToken);

        return rows.Select(r => new HallEventListItemDto(
            r.Id.Value, r.CustomerId.Value, null, r.Date, r.Start, r.End, r.Attendance.Value, r.Total, r.Status.ToString(),
            r.DecisionDeadlineUtc, r.DepositDeadlineUtc, r.Required, r.Accredited)).ToList();
    }

    private static HallEventDto ToDto(HallEvent e) => new(
        e.Id.Value, e.QuoteId.Value, e.CustomerId.Value, null, e.HallId.Value, e.Slot.Date, e.Slot.Start, e.Slot.End,
        e.Attendance.Value, e.Total.Amount, e.Status.ToString(),
        e.Deposit is null
            ? null
            : new DepositDto(
                e.Deposit.Percentage, e.Deposit.Required.Amount, e.Deposit.Accredited.Amount, e.Deposit.Pending.Amount,
                e.Deposit.IsCovered, e.Deposit.TransactionIds),
        e.RequestedAtUtc, e.DecisionDeadlineUtc, e.DepositDeadlineUtc,
        e.Decision is null
            ? null
            : new ManagementDecisionDto(e.Decision.DecidedBy, e.Decision.DecidedAtUtc, e.Decision.Approved, e.Decision.RejectionReason),
        e.CreditGenerated.Amount, e.RescheduleCount, e.ExtraServicesAssigned, e.RemainingToSettle?.Amount,
        e.ExtraServices
            .Select(x => new ExtraServiceDto(
                x.Type.ToString(), x.Code, x.Description, x.Quantity, x.UnitCost.Amount, x.LineTotal.Amount))
            .ToList(),
        e.History.Select(h => new HistoryEntryDto(h.Status.ToString(), h.OccurredAtUtc, h.Actor, h.Note)).ToList());
}
