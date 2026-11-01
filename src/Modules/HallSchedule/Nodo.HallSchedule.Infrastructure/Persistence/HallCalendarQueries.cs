using Microsoft.EntityFrameworkCore;
using Nodo.HallSchedule.Application;
using Nodo.HallSchedule.Contracts;
using Nodo.HallSchedule.Domain;

namespace Nodo.HallSchedule.Infrastructure.Persistence;

internal sealed class HallCalendarQueries(HallScheduleDbContext db) : IHallCalendarQueries
{
    public async Task<IReadOnlyCollection<ScheduleBlockReadModel>> GetActiveBlocksAsync(
        Guid hallId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var hall = new HallId(hallId);

        var rows = await db.Schedules
            .AsNoTracking()
            .Where(s => s.HallId == hall && s.Date >= from && s.Date <= to)
            .SelectMany(s => s.Blocks)
            .Where(b => b.Status == BlockStatus.Active)
            .OrderBy(b => b.Slot.Date).ThenBy(b => b.Slot.Start)
            .Select(b => new
            {
                BlockId = b.Id,
                EventId = b.EventId,
                b.Slot.Date,
                b.Slot.Start,
                b.Slot.End,
                b.Kind,
                Equipment = b.EquipmentAllocations.Select(e => new { e.Code, e.Quantity }).ToList()
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new ScheduleBlockReadModel(
                r.BlockId.Value, r.EventId.Value, r.Date, r.Start, r.End, r.Kind.ToString(),
                r.Equipment.Select(e => new EquipmentRequest(e.Code, e.Quantity)).ToList()))
            .ToList();
    }
}
