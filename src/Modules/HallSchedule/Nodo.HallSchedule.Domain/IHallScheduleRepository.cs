namespace Nodo.HallSchedule.Domain;

public interface IHallScheduleRepository
{
    /// <summary>Loads the day's schedule, or returns a new empty one (not yet persisted).</summary>
    Task<HallSchedule> GetOrCreateAsync(HallId hallId, DateOnly date, CancellationToken cancellationToken = default);

    Task SaveAsync(HallSchedule schedule, CancellationToken cancellationToken = default);
}
