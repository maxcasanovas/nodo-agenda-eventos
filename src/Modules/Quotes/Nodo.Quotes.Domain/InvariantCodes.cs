namespace Nodo.Quotes.Domain;

/// <summary>Stable codes returned in the 422 response (business-rules.md §9).</summary>
public static class InvariantCodes
{
    // Same string codes as EventLifecycle: the catalog defines one code per invariant, whichever module raises it.
    public const string DurationOutOfRange = "EVENT.DURATION_OUT_OF_RANGE";                // INV-01
    public const string AttendanceOutOfRange = "EVENT.ATTENDANCE_OUT_OF_RANGE";            // INV-04
    public const string ExtraServiceInvalid = "EVENT.EXTRA_SERVICE_INVALID";               // INV-13 (BR-08 also applies when quoting)

    public const string Expired = "QUOTE.EXPIRED";                                          // INV-14
    // INV-15: price and services are frozen by design (no method changes them after acceptance)
    public const string SlotOutsideOperatingHours = "QUOTE.SLOT_OUTSIDE_OPERATING_HOURS";   // INV-16
    public const string DateOutOfRange = "QUOTE.DATE_OUT_OF_RANGE";                         // INV-16
    public const string InvalidDiscount = "QUOTE.INVALID_DISCOUNT";                         // INV-17

    // Not in the catalog: needed for accepting a quote that is already accepted.
    public const string InvalidStateTransition = "QUOTE.INVALID_STATE_TRANSITION";
}
