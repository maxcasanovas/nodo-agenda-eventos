namespace Nodo.HallSchedule.Domain;

public readonly record struct HallId(Guid Value)
{
    public static HallId New() => new(Guid.NewGuid());
}

public readonly record struct EventId(Guid Value)
{
    public static EventId New() => new(Guid.NewGuid());
}

public readonly record struct ScheduleBlockId(Guid Value)
{
    public static ScheduleBlockId New() => new(Guid.NewGuid());
}

/// <summary>A schedule is one aggregate per hall and day.</summary>
public readonly record struct HallScheduleId(HallId HallId, DateOnly Date);
