namespace Nodo.HallSchedule.Domain;

public sealed record AvailabilityResult(bool IsAvailable, string? Code, string? Reason)
{
    public static AvailabilityResult Available { get; } = new(true, null, null);

    public static AvailabilityResult Unavailable(string code, string reason) => new(false, code, reason);
}

/// <summary>
/// Domain service (BR-03, BR-08/INV-20). Answers whether a slot and equipment can be placed
/// on a day's schedule, without mutating anything.
/// </summary>
public static class AvailabilityChecker
{
    public static AvailabilityResult Check(
        HallSchedule schedule,
        TimeSlot slot,
        IReadOnlyCollection<EquipmentAllocation> requestedEquipment,
        IReadOnlyDictionary<string, int> inventory,
        EventId? excludeEventId = null)
    {
        if (slot.Date != schedule.Date)
            throw new ArgumentException("The slot belongs to a different day than the schedule.", nameof(slot));

        // INV-18. The excluded event's own block does not count (e.g. moving an event within the same day).
        var conflict = schedule.ActiveBlocks
            .Where(b => excludeEventId is null || b.EventId != excludeEventId)
            .FirstOrDefault(b => b.Slot.OverlapsWithBuffers(slot));
        if (conflict is not null)
        {
            return AvailabilityResult.Unavailable(InvariantCodes.SlotOverlap,
                $"El salón no está disponible: la franja {slot.Start:HH\\:mm}-{slot.End:HH\\:mm} se solapa con otra reserva " +
                $"({conflict.Slot.Start:HH\\:mm}-{conflict.Slot.End:HH\\:mm}) considerando 1 h de montaje y 1 h de limpieza.");
        }

        // INV-20
        foreach (var group in requestedEquipment.GroupBy(e => e.Code))
        {
            var requested = group.Sum(e => e.Quantity);
            var stock = inventory.TryGetValue(group.Key, out var s) ? s : 0;
            if (requested > stock)
            {
                return AvailabilityResult.Unavailable(InvariantCodes.EquipmentUnavailable,
                    $"Equipo no disponible: se pidieron {requested} de '{group.Key}' y el inventario es {stock}.");
            }
        }

        return AvailabilityResult.Available;
    }
}
