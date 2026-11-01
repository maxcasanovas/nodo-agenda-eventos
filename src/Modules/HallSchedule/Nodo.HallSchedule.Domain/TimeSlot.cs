using Nodo.SharedKernel;

namespace Nodo.HallSchedule.Domain;

/// <summary>Local-time slot of an event (INV-01 / BR-01): whole hours, 1 to 8, same day.</summary>
public sealed class TimeSlot : ValueObject
{
    public const int MinHours = 1;
    public const int MaxHours = 8;
    public const int BufferMinutes = 60; // BR-03

    private TimeSlot(DateOnly date, TimeOnly start, TimeOnly end)
    {
        Date = date;
        Start = start;
        End = end;
    }

    public DateOnly Date { get; }

    public TimeOnly Start { get; }

    public TimeOnly End { get; }

    public int DurationHours => End.Hour - Start.Hour;

    /// <summary>Minutes from midnight including the setup buffer. Plain ints avoid TimeOnly overflow at 23:00.</summary>
    public int BufferedStartMinute => Start.Hour * 60 - BufferMinutes;

    public int BufferedEndMinute => End.Hour * 60 + BufferMinutes;

    public static TimeSlot Create(DateOnly date, TimeOnly start, TimeOnly end)
    {
        if (!IsExactHour(start) || !IsExactHour(end))
            throw Invalid("El inicio y el fin del evento deben ser en hora exacta (HH:00).");
        if (end <= start)
            throw Invalid("El evento debe terminar el mismo día, después de su hora de inicio.");

        var hours = end.Hour - start.Hour;
        if (hours is < MinHours or > MaxHours)
            throw Invalid($"La duración debe ser de {MinHours} a {MaxHours} horas enteras (se indicó {hours}).");

        return new TimeSlot(date, start, end);
    }

    /// <summary>BR-03: ranges including setup and cleanup buffers must not intersect.</summary>
    public bool OverlapsWithBuffers(TimeSlot other) =>
        Date == other.Date &&
        BufferedStartMinute < other.BufferedEndMinute &&
        other.BufferedStartMinute < BufferedEndMinute;

    private static bool IsExactHour(TimeOnly time) => time.Ticks % TimeSpan.TicksPerHour == 0;

    private static DomainException Invalid(string message) => new(InvariantCodes.DurationOutOfRange, message);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Date;
        yield return Start;
        yield return End;
    }
}
