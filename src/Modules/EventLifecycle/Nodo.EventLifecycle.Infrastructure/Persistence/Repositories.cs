using Microsoft.EntityFrameworkCore;
using Nodo.EventLifecycle.Domain;

namespace Nodo.EventLifecycle.Infrastructure.Persistence;

internal sealed class HallEventRepository(EventLifecycleDbContext db) : IHallEventRepository
{
    private static readonly HallEventStatus[] ActiveStatuses =
        [HallEventStatus.Requested, HallEventStatus.ApprovedByManagement, HallEventStatus.Confirmed, HallEventStatus.InProgress];

    public async Task<HallEvent?> GetByIdAsync(HallEventId id, CancellationToken cancellationToken = default) =>
        await db.HallEvents.Include(e => e.ExtraServices).FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task AddAsync(HallEvent hallEvent, CancellationToken cancellationToken = default) =>
        await db.HallEvents.AddAsync(hallEvent, cancellationToken);

    public async Task<bool> ExistsActiveForSlotAsync(
        HallId hallId, TimeSlot slot, CancellationToken cancellationToken = default)
    {
        // Same overlap rule as the schedule (BR-03): each range grows by one hour of setup and one of cleanup.
        var startHour = slot.Start.Hour;
        var endHour = slot.End.Hour;
        return await db.HallEvents.AnyAsync(e =>
            e.HallId == hallId && e.Slot.Date == slot.Date && ActiveStatuses.Contains(e.Status) &&
            e.Slot.Start.Hour - 1 < endHour + 1 && startHour - 1 < e.Slot.End.Hour + 1, cancellationToken);
    }

    public async Task<IReadOnlyCollection<HallEvent>> ListOverdueAsync(
        DateTime nowUtc, CancellationToken cancellationToken = default) =>
        await db.HallEvents
            .Where(e =>
                (e.Status == HallEventStatus.Requested && e.DecisionDeadlineUtc <= nowUtc) ||
                (e.Status == HallEventStatus.ApprovedByManagement && e.DepositDeadlineUtc <= nowUtc))
            .ToListAsync(cancellationToken);
}

internal sealed class CustomerCreditRepository(EventLifecycleDbContext db) : ICustomerCreditRepository
{
    public async Task<CustomerCredit?> GetByCustomerAsync(CustomerId customerId, CancellationToken cancellationToken = default) =>
        await db.CustomerCredits.FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);

    public async Task AddAsync(CustomerCredit credit, CancellationToken cancellationToken = default) =>
        await db.CustomerCredits.AddAsync(credit, cancellationToken);
}
