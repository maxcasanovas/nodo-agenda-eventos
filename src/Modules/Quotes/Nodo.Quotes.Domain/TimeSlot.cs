using Nodo.SharedKernel;

namespace Nodo.Quotes.Domain;

/// <summary>Local-time slot (INV-01 / BR-01): whole hours, 1 to 8, same day.</summary>
public sealed class TimeSlot : ValueObject
{
    public const int MinHours = 1;
    public const int MaxHours = 8;

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

    private static bool IsExactHour(TimeOnly time) => time.Ticks % TimeSpan.TicksPerHour == 0;

    private static DomainException Invalid(string message) => new(InvariantCodes.DurationOutOfRange, message);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Date;
        yield return Start;
        yield return End;
    }
}
