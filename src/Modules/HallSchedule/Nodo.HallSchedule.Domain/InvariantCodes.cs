namespace Nodo.HallSchedule.Domain;

/// <summary>Stable codes returned in the 422 response (business-rules.md §9).</summary>
public static class InvariantCodes
{
    public const string DurationOutOfRange = "EVENT.DURATION_OUT_OF_RANGE";      // INV-01 (same code in every module: one code per invariant)
    public const string SlotOverlap = "SCHEDULE.SLOT_OVERLAP";                    // INV-18
    public const string DuplicateBlock = "SCHEDULE.DUPLICATE_BLOCK";              // INV-19
    public const string EquipmentUnavailable = "SCHEDULE.EQUIPMENT_UNAVAILABLE";  // INV-20
}
