using Nodo.SharedKernel;

namespace Nodo.Quotes.Domain;

/// <summary>Operating hours and booking window (BR-02, BR-03, BR-05). INV-16.</summary>
public static class BookingPolicy
{
    public const int OpeningHour = 7;   // BR-02 [Inventado]
    public const int ClosingHour = 23;
    public const int BufferHours = 1;   // BR-03
    public const int MinDaysAhead = 3;  // BR-05 [Inventado]
    public const int MaxDaysAhead = 180;

    public static void EnsureWithinOperatingHours(TimeSlot slot)
    {
        // The range including setup and cleanup buffers must fit in 07:00-23:00.
        if (slot.Start.Hour - BufferHours < OpeningHour || slot.End.Hour + BufferHours > ClosingHour)
        {
            throw new DomainException(InvariantCodes.SlotOutsideOperatingHours,
                $"El salón opera de {OpeningHour:00}:00 a {ClosingHour:00}:00 incluyendo {BufferHours} h de montaje y " +
                $"{BufferHours} h de limpieza: el evento debe empezar desde las {OpeningHour + BufferHours:00}:00 y " +
                $"terminar a más tardar a las {ClosingHour - BufferHours:00}:00.");
        }
    }

    public static void EnsureWithinBookingWindow(DateOnly eventDate, DateOnly todayLocal)
    {
        var earliest = todayLocal.AddDays(MinDaysAhead);
        var latest = todayLocal.AddDays(MaxDaysAhead);
        if (eventDate < earliest || eventDate > latest)
        {
            throw new DomainException(InvariantCodes.DateOutOfRange,
                $"La fecha del evento debe estar entre {earliest:dd/MM/yyyy} y {latest:dd/MM/yyyy} " +
                $"({MinDaysAhead} a {MaxDaysAhead} días desde hoy).");
        }
    }
}
