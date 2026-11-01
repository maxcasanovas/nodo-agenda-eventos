using Microsoft.EntityFrameworkCore;
using Nodo.HallSchedule.Domain;

namespace Nodo.HallSchedule.Infrastructure.Persistence;

internal sealed class HallScheduleRepository(HallScheduleDbContext db) : IHallScheduleRepository
{
    public async Task<HallScheduleAggregate> GetOrCreateAsync(
        HallId hallId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var schedule = await db.Schedules
            .Include(s => s.Blocks)
            .FirstOrDefaultAsync(s => s.HallId == hallId && s.Date == date, cancellationToken);

        return schedule ?? HallScheduleAggregate.Create(hallId, date);
    }

    public Task SaveAsync(HallScheduleAggregate schedule, CancellationToken cancellationToken = default)
    {
        // Loaded schedules are already tracked; only a newly created day needs to be added.
        if (db.Entry(schedule).State == EntityState.Detached)
            db.Schedules.Add(schedule);

        return Task.CompletedTask;
    }
}
