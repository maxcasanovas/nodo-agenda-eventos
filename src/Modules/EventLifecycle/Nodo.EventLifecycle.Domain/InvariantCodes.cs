namespace Nodo.EventLifecycle.Domain;

/// <summary>Stable codes returned in the 422 response (business-rules.md §9).</summary>
public static class InvariantCodes
{
    public const string DurationOutOfRange = "EVENT.DURATION_OUT_OF_RANGE";                         // INV-01
    public const string DepositPercentageOutOfRange = "EVENT.DEPOSIT_PERCENTAGE_OUT_OF_RANGE";      // INV-02
    public const string ConfirmationPreconditionsNotMet = "EVENT.CONFIRMATION_PRECONDITIONS_NOT_MET"; // INV-03
    public const string AttendanceOutOfRange = "EVENT.ATTENDANCE_OUT_OF_RANGE";                     // INV-04
    public const string CancellationNotAllowed = "EVENT.CANCELLATION_NOT_ALLOWED";                  // INV-05
    public const string InvalidStateTransition = "EVENT.INVALID_STATE_TRANSITION";                  // INV-06
    public const string DecisionDeadlineExceeded = "EVENT.DECISION_DEADLINE_EXCEEDED";              // INV-07
    public const string RejectionReasonRequired = "EVENT.REJECTION_REASON_REQUIRED";                // INV-08
    public const string DepositNotAccepted = "EVENT.DEPOSIT_NOT_ACCEPTED";                          // INV-09
    // INV-10 (MONEY.CURRENCY_MISMATCH / MONEY.NEGATIVE_AMOUNT) lives in Nodo.SharedKernel.InvariantCodes
    public const string StartOutsideWindow = "EVENT.START_OUTSIDE_WINDOW";                          // INV-11
    public const string RescheduleNotAllowed = "EVENT.RESCHEDULE_NOT_ALLOWED";                      // INV-12
    public const string ExtraServiceInvalid = "EVENT.EXTRA_SERVICE_INVALID";                        // INV-13
    public const string CreditInsufficient = "CREDIT.INSUFFICIENT";                                 // INV-21

    // INV-16 applies again when rescheduling (BR-23 requires BR-01..05): same codes as Quotes.
    public const string SlotOutsideOperatingHours = "QUOTE.SLOT_OUTSIDE_OPERATING_HOURS";
    public const string DateOutOfRange = "QUOTE.DATE_OUT_OF_RANGE";
}
