using Nodo.SharedKernel;

namespace Nodo.EventLifecycle.Domain;

/// <summary>
/// Aggregate root of the event lifecycle (Requested to Completed, plus cancellation and rescheduling).
/// Every command validates first and mutates afterwards, so a failed command leaves the aggregate untouched.
/// </summary>
public sealed class HallEvent : AggregateRoot<HallEventId>
{
    public const int DepositWindowHours = 48;       // BR-17 [Inventado]
    public const int StartAllowedHoursBefore = 1;   // BR-18
    public const int MinRejectionReasonLength = 10; // BR-13
    private const string SystemActor = "Sistema";

    private readonly List<ExtraServiceItem> _extraServices = [];
    private readonly List<HistoryEntry> _history = [];

    private HallEvent() : base(default)
    {
        Slot = null!;
        Attendance = null!;
        Total = null!;
        CreditGenerated = null!;
    }

    private HallEvent(
        HallEventId id, QuoteId quoteId, CustomerId customerId, HallId hallId, TimeSlot slot, Attendance attendance,
        Money total, IEnumerable<ExtraServiceItem> extraServices, DateTime requestedAtUtc) : base(id)
    {
        QuoteId = quoteId;
        CustomerId = customerId;
        HallId = hallId;
        Slot = slot;
        Attendance = attendance;
        Total = total;
        _extraServices.AddRange(extraServices);
        Status = HallEventStatus.Requested;
        RequestedAtUtc = requestedAtUtc;
        DecisionDeadlineUtc = EndOfLocalDayUtc(requestedAtUtc);
        CreditGenerated = Money.Zero;
    }

    public QuoteId QuoteId { get; private set; }

    public CustomerId CustomerId { get; private set; }

    public HallId HallId { get; private set; }

    public TimeSlot Slot { get; private set; }

    public Attendance Attendance { get; private set; }

    public Money Total { get; private set; }

    public IReadOnlyCollection<ExtraServiceItem> ExtraServices => _extraServices.AsReadOnly();

    public HallEventStatus Status { get; private set; }

    /// <summary>Null until Management approves.</summary>
    public GuaranteeDeposit? Deposit { get; private set; }

    public DateTime RequestedAtUtc { get; private set; }

    /// <summary>Exclusive: Management must decide strictly before this instant (start of the next local day).</summary>
    public DateTime DecisionDeadlineUtc { get; private set; }

    /// <summary>Exclusive. Null when no deposit is pending.</summary>
    public DateTime? DepositDeadlineUtc { get; private set; }

    public ManagementDecision? Decision { get; private set; }

    public Money CreditGenerated { get; private set; }

    public int RescheduleCount { get; private set; }

    public bool ExtraServicesAssigned { get; private set; }

    public Money? RemainingToSettle { get; private set; }

    public IReadOnlyList<HistoryEntry> History => _history.AsReadOnly();

    internal static HallEvent CreateRequested(
        QuoteId quoteId, CustomerId customerId, HallId hallId, TimeSlot slot, Attendance attendance,
        Money total, IEnumerable<ExtraServiceItem> extraServices, DateTime nowUtc)
    {
        var hallEvent = new HallEvent(
            HallEventId.New(), quoteId, customerId, hallId, slot, attendance, total, extraServices, nowUtc);
        hallEvent.AddHistory("Cliente", "Solicitud creada a partir de una cotización aceptada.", nowUtc);
        hallEvent.RaiseDomainEvent(new EventRequestCreated(
            hallEvent.Id, hallId, slot.Date, slot.Start, slot.End, attendance.Value, customerId, nowUtc));
        return hallEvent;
    }

    /// <summary>Management approves and sets the deposit percentage (BR-12, BR-14).</summary>
    public void Approve(string by, int depositPercentage, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(by);
        EnsureStatus("aprobar", HallEventStatus.Requested);
        EnsureWithinDecisionDeadline(nowUtc);
        var deposit = GuaranteeDeposit.Create(depositPercentage, Total); // INV-02

        Deposit = deposit;
        DepositDeadlineUtc = nowUtc.AddHours(DepositWindowHours);
        Decision = ManagementDecision.Approve(by.Trim(), nowUtc);
        Transition(HallEventStatus.ApprovedByManagement, by,
            $"Aprobado con depósito de {depositPercentage} % ({deposit.Required}).", nowUtc);
        RaiseDomainEvent(new HallEventApprovedByManagement(
            Id, by.Trim(), depositPercentage, deposit.Required, DepositDeadlineUtc.Value, nowUtc));
    }

    /// <summary>Management rejects with a reason of at least 10 characters (BR-13).</summary>
    public void Reject(string by, string? reason, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(by);
        EnsureStatus("rechazar", HallEventStatus.Requested);
        EnsureWithinDecisionDeadline(nowUtc);
        var trimmed = reason?.Trim() ?? string.Empty;
        if (trimmed.Length < MinRejectionReasonLength) // INV-08
        {
            throw new DomainException(InvariantCodes.RejectionReasonRequired,
                $"El rechazo requiere un motivo de al menos {MinRejectionReasonLength} caracteres.");
        }

        Decision = ManagementDecision.Reject(by.Trim(), trimmed, nowUtc);
        Transition(HallEventStatus.Rejected, by, $"Rechazado: {trimmed}", nowUtc);
        RaiseDomainEvent(new EventRequestRejected(Id, HallId, Slot.Date, by.Trim(), trimmed, nowUtc));
    }

    /// <summary>
    /// Accredits a payment (BR-15). Payments accumulate and are idempotent per transaction id.
    /// When the deposit becomes covered the event is confirmed in the same call (BR-16).
    /// </summary>
    /// <returns>Excess over the required deposit, to be granted as customer credit (zero if none).</returns>
    public Money AccreditDeposit(Money amount, string transactionId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(transactionId))
            throw new DomainException(InvariantCodes.DepositNotAccepted, "El pago requiere un identificador de transacción.");

        // A repeated transaction is idempotent, not an error (INV-09), whatever the current state.
        if (Deposit is not null && Deposit.HasTransaction(transactionId))
            return Money.Of(0m, amount.Currency);

        if (Status != HallEventStatus.ApprovedByManagement || Deposit is null)
        {
            throw new DomainException(InvariantCodes.DepositNotAccepted,
                $"Solo se acredita el depósito en un evento aprobado por Dirección (estado actual: {Status}).");
        }

        if (nowUtc >= DepositDeadlineUtc)
        {
            throw new DomainException(InvariantCodes.DepositNotAccepted,
                "El plazo de 48 h para pagar el depósito de garantía ya venció.");
        }

        var result = Deposit.Accredit(amount, transactionId); // INV-10 (currency / negative)

        Deposit = result.Deposit;
        RaiseDomainEvent(new GuaranteeDepositAccredited(
            Id, amount, transactionId, result.Deposit.Accredited, result.Deposit.IsCovered, nowUtc));

        if (result.Deposit.IsCovered)
            ConfirmFirmly(nowUtc);

        return result.Excess;
    }

    /// <summary>Firm confirmation: requires Management approval and a covered deposit (INV-03, BR-16).</summary>
    public void ConfirmFirmly(DateTime nowUtc)
    {
        if (Status is not (HallEventStatus.Requested or HallEventStatus.ApprovedByManagement))
            throw InvalidTransition("confirmar en firme");

        if (Status != HallEventStatus.ApprovedByManagement || Deposit is null || !Deposit.IsCovered)
        {
            throw new DomainException(InvariantCodes.ConfirmationPreconditionsNotMet,
                "La confirmación en firme requiere la aprobación de Dirección y el depósito de garantía cubierto.");
        }

        DepositDeadlineUtc = null;
        Transition(HallEventStatus.Confirmed, SystemActor, "Depósito cubierto: evento confirmado en firme.", nowUtc);
        RaiseDomainEvent(new HallEventFirmlyConfirmed(
            Id, HallId, Slot.Date, Slot.Start, Slot.End, Attendance.Value, CustomerId, Total,
            Deposit.Accredited, ExtraServices, nowUtc));
    }

    /// <summary>BR-20. Idempotent.</summary>
    public void AssignExtraServices(DateTime nowUtc)
    {
        EnsureStatus("asignar servicios extra", HallEventStatus.Confirmed);
        if (ExtraServicesAssigned)
            return;

        ExtraServicesAssigned = true;
        RaiseDomainEvent(new ExtraServicesAssigned(Id, ExtraServices, nowUtc));
    }

    /// <summary>BR-18: from one hour before the start until the end.</summary>
    public void Start(DateTime nowUtc, string performedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(performedBy);
        EnsureStatus("iniciar", HallEventStatus.Confirmed);

        var opensAt = Slot.StartUtc.AddHours(-StartAllowedHoursBefore);
        if (nowUtc < opensAt || nowUtc >= Slot.EndUtc)
        {
            throw new DomainException(InvariantCodes.StartOutsideWindow,
                $"El evento solo puede iniciarse desde {StartAllowedHoursBefore} h antes de su hora de inicio y antes de que termine.");
        }

        Transition(HallEventStatus.InProgress, performedBy, "Evento iniciado.", nowUtc);
        RaiseDomainEvent(new HallEventStarted(Id, performedBy.Trim(), nowUtc));
    }

    /// <summary>BR-19. Exposes <see cref="RemainingToSettle"/> = Total - deposit accredited.</summary>
    public void Complete(DateTime nowUtc, string performedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(performedBy);
        EnsureStatus("completar", HallEventStatus.InProgress);

        var accredited = Deposit?.Accredited ?? Money.Zero;
        var remaining = Total.Subtract(accredited);

        RemainingToSettle = remaining;
        Transition(HallEventStatus.Completed, performedBy, $"Evento completado. Saldo a liquidar: {remaining}.", nowUtc);
        RaiseDomainEvent(new HallEventCompleted(Id, CustomerId, Total, accredited, remaining, nowUtc));
    }

    /// <summary>BR-21 / INV-05: no refund; everything accredited becomes credit.</summary>
    public void Cancel(string by, string? reason, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(by);
        if (Status is not (HallEventStatus.Requested or HallEventStatus.ApprovedByManagement or HallEventStatus.Confirmed))
        {
            throw new DomainException(InvariantCodes.CancellationNotAllowed,
                $"No se puede cancelar un evento en estado {Status}.");
        }

        if (nowUtc >= Slot.StartUtc)
        {
            throw new DomainException(InvariantCodes.CancellationNotAllowed,
                "No se puede cancelar un evento que ya alcanzó su hora de inicio.");
        }

        var credit = CancellationPolicy.CreditFor(Deposit);

        CreditGenerated = credit;
        DepositDeadlineUtc = null;
        Transition(HallEventStatus.Cancelled, by, CancellationNote(reason, credit), nowUtc);
        RaiseDomainEvent(new HallEventCancelledWithCredit(Id, HallId, Slot.Date, CustomerId, credit, nowUtc));
    }

    /// <summary>
    /// Expires an unattended request (BR-12) or an unpaid deposit (BR-17). Idempotent.
    /// A partially paid deposit becomes credit.
    /// </summary>
    public void Expire(DateTime nowUtc)
    {
        if (Status == HallEventStatus.Expired)
            return;

        var overdue = Status switch
        {
            HallEventStatus.Requested => nowUtc >= DecisionDeadlineUtc,
            HallEventStatus.ApprovedByManagement => nowUtc >= DepositDeadlineUtc,
            _ => throw InvalidTransition("expirar")
        };
        if (!overdue)
            throw InvalidTransition("expirar antes de su plazo");

        var note = Status == HallEventStatus.Requested
            ? "Dirección no respondió dentro del día de la solicitud."
            : "No se cubrió el depósito de garantía dentro de las 48 h.";
        var credit = CancellationPolicy.CreditFor(Deposit);

        CreditGenerated = credit;
        DepositDeadlineUtc = null;
        Transition(HallEventStatus.Expired, SystemActor, credit.Amount > 0 ? $"{note} Lo acreditado ({credit}) pasa a saldo a favor." : note, nowUtc);
        RaiseDomainEvent(new HallEventExpired(Id, HallId, Slot.Date, CustomerId, credit, nowUtc));
    }

    /// <summary>
    /// BR-23: reschedules a cancelled event using customer credit. Same duration, attendance, services and total;
    /// no new Management approval. Ends Confirmed if the credit covers the deposit, otherwise ApprovedByManagement.
    /// </summary>
    /// <param name="creditApplied">Credit taken from the customer's balance, at most the required deposit.</param>
    public void Reschedule(TimeSlot newSlot, Money creditApplied, string by, DateTime nowUtc, DateOnly todayLocal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(by);
        EnsureCanReschedule(newSlot, todayLocal);

        var previous = Deposit!; // guaranteed by EnsureCanReschedule
        if (creditApplied.Amount <= 0 || creditApplied.Amount > previous.Required.Amount)
        {
            throw new DomainException(InvariantCodes.RescheduleNotAllowed,
                $"El saldo a favor aplicado debe ser mayor a cero y no superar el depósito requerido ({previous.Required}).");
        }

        var paymentId = $"credit-{Id.Value}-{RescheduleCount + 1}";
        var deposit = GuaranteeDeposit.Create(previous.Percentage, Total).Accredit(creditApplied, paymentId).Deposit;
        var covered = deposit.IsCovered;

        Slot = newSlot;
        Deposit = deposit;
        CreditGenerated = Money.Zero; // consumed: it now lives in the customer's credit balance
        RescheduleCount++;
        ExtraServicesAssigned = false;
        DepositDeadlineUtc = covered ? null : nowUtc.AddHours(DepositWindowHours);

        var status = covered ? HallEventStatus.Confirmed : HallEventStatus.ApprovedByManagement;
        Transition(status, by,
            $"Reprogramado al {newSlot.Date:dd/MM/yyyy} {newSlot.Start:HH\\:mm}-{newSlot.End:HH\\:mm} aplicando {creditApplied} de saldo a favor" +
            (covered ? "." : $"; faltan {deposit.Pending} para confirmar."), nowUtc);
        RaiseDomainEvent(new HallEventRescheduledWithCredit(
            Id, HallId, CustomerId, newSlot.Date, newSlot.Start, newSlot.End, creditApplied, status,
            deposit.Pending, DepositDeadlineUtc, nowUtc));
        if (covered)
        {
            RaiseDomainEvent(new HallEventFirmlyConfirmed(
                Id, HallId, Slot.Date, Slot.Start, Slot.End, Attendance.Value, CustomerId, Total,
                deposit.Accredited, ExtraServices, nowUtc));
        }
    }

    /// <summary>
    /// BR-23: the new slot keeps the original duration; the caller only chooses the day and the start time.
    /// </summary>
    public TimeSlot SlotMovedTo(DateOnly newDate, TimeOnly newStart)
    {
        var endHour = newStart.Hour + Slot.DurationHours;
        if (endHour > 23)
        {
            throw new DomainException(InvariantCodes.SlotOutsideOperatingHours,
                $"Un evento de {Slot.DurationHours} h que empieza a las {newStart:HH\\:mm} terminaría después de la medianoche.");
        }

        return TimeSlot.Create(newDate, newStart, new TimeOnly(endHour, 0));
    }

    /// <summary>
    /// The event-side preconditions of <see cref="Reschedule"/> (INV-12, BR-01..05), without touching the credit.
    /// Lets the caller fail early, before reserving anything elsewhere. After it passes, <see cref="Deposit"/> is set.
    /// </summary>
    public void EnsureCanReschedule(TimeSlot newSlot, DateOnly todayLocal)
    {
        ArgumentNullException.ThrowIfNull(newSlot);

        if (Status != HallEventStatus.Cancelled || CreditGenerated.Amount <= 0 || Deposit is null) // INV-12
        {
            throw new DomainException(InvariantCodes.RescheduleNotAllowed,
                "Solo se puede reprogramar un evento cancelado que generó saldo a favor.");
        }

        if (newSlot.DurationHours != Slot.DurationHours)
        {
            throw new DomainException(InvariantCodes.RescheduleNotAllowed,
                $"La reprogramación debe conservar la duración original ({Slot.DurationHours} h).");
        }

        BookingPolicy.EnsureWithinOperatingHours(newSlot);                  // BR-02
        BookingPolicy.EnsureWithinBookingWindow(newSlot.Date, todayLocal);  // BR-05
    }

    private void EnsureStatus(string action, params HallEventStatus[] allowed)
    {
        if (!allowed.Contains(Status))
            throw InvalidTransition(action);
    }

    private void EnsureWithinDecisionDeadline(DateTime nowUtc)
    {
        if (nowUtc >= DecisionDeadlineUtc) // INV-07
        {
            throw new DomainException(InvariantCodes.DecisionDeadlineExceeded,
                "Dirección debía decidir antes de las 23:59:59 del día de la solicitud; el plazo venció.");
        }
    }

    private DomainException InvalidTransition(string action) =>
        new(InvariantCodes.InvalidStateTransition, $"No se puede {action} un evento en estado {Status}.");

    private void Transition(HallEventStatus to, string actor, string note, DateTime nowUtc)
    {
        Status = to;
        AddHistory(actor, note, nowUtc);
    }

    private void AddHistory(string actor, string note, DateTime nowUtc) =>
        _history.Add(HistoryEntry.Create(Status, nowUtc, actor, note));

    private static string CancellationNote(string? reason, Money credit)
    {
        var why = string.IsNullOrWhiteSpace(reason) ? "Cancelado." : $"Cancelado: {reason.Trim()}.";
        return credit.Amount > 0
            ? $"{why} Sin devolución de dinero: {credit} pasan íntegros a saldo a favor."
            : why;
    }

    /// <summary>00:00 of the next local day, expressed in UTC (BR-12).</summary>
    private static DateTime EndOfLocalDayUtc(DateTime requestedAtUtc)
    {
        var localDate = DateOnly.FromDateTime(NodoTimeZone.ToLocal(requestedAtUtc));
        return NodoTimeZone.ToUtc(localDate.AddDays(1), TimeOnly.MinValue);
    }
}
