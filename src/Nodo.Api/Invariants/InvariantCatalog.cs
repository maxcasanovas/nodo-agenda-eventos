namespace Nodo.Api.Invariants;

/// <summary>One entry of the invariant catalog (business-rules.md §9) as the API publishes it.</summary>
/// <param name="Id">INV-xx.</param>
/// <param name="Description">What the rule says, in Spanish.</param>
/// <param name="Owner">The aggregate or value object that enforces it.</param>
/// <param name="Code">The stable code returned with the 422 (two codes when the rule has two ways to fail).</param>
/// <param name="DatabaseEnforced">Whether PostgreSQL also guards it (CHECK, EXCLUDE or unique index).</param>
/// <param name="ByDesign">True when the rule is kept by not offering the operation at all, so it has no error to show.</param>
/// <param name="ExampleMessage">A realistic Spanish message for the documentation.</param>
public sealed record InvariantInfo(
    string Id, string Description, string Owner, string Code, bool DatabaseEnforced, bool ByDesign, string ExampleMessage);

public static class InvariantCatalog
{
    public static IReadOnlyList<InvariantInfo> All { get; } =
    [
        new("INV-01", "La duración del evento es de 1 a 8 horas enteras, continuas y dentro del mismo día (BR-01).",
            "TimeSlot", "EVENT.DURATION_OUT_OF_RANGE", true, false,
            "La duración debe ser de 1 a 8 horas enteras (se indicó 9)."),
        new("INV-02", "El depósito de garantía exigido está entre el 20 % y el 50 % del total (BR-14).",
            "GuaranteeDeposit", "EVENT.DEPOSIT_PERCENTAGE_OUT_OF_RANGE", true, false,
            "El depósito de garantía debe ser entre 20 % y 50 % del total (se indicó 19 %)."),
        new("INV-03", "La confirmación en firme exige la aprobación de Dirección y el depósito cubierto (BR-16).",
            "HallEvent.ConfirmFirmly", "EVENT.CONFIRMATION_PRECONDITIONS_NOT_MET", false, false,
            "La confirmación en firme requiere la aprobación de Dirección y el depósito de garantía cubierto."),
        new("INV-04", "El aforo es un entero entre 1 y 80 personas (BR-04).",
            "Attendance", "EVENT.ATTENDANCE_OUT_OF_RANGE", true, false,
            "El aforo debe estar entre 1 y 80 personas (se indicó 81)."),
        new("INV-05", "Cancelar un evento con depósito no devuelve dinero: todo lo acreditado pasa íntegro a saldo a favor (BR-21).",
            "HallEvent.Cancel + CancellationPolicy", "EVENT.CANCELLATION_NOT_ALLOWED", true, false,
            "No se puede cancelar un evento en estado Rejected."),
        new("INV-06", "Solo existen las transiciones válidas de la máquina de estados del evento.",
            "HallEvent", "EVENT.INVALID_STATE_TRANSITION", false, false,
            "No se puede aprobar un evento en estado ApprovedByManagement."),
        new("INV-07", "Dirección decide dentro del día de la solicitud (hasta las 23:59:59 locales) (BR-12).",
            "HallEvent.Approve / Reject", "EVENT.DECISION_DEADLINE_EXCEEDED", false, false,
            "Dirección debía decidir antes de las 23:59:59 del día de la solicitud; el plazo venció."),
        new("INV-08", "El rechazo requiere un motivo de al menos 10 caracteres (BR-13).",
            "HallEvent.Reject", "EVENT.REJECTION_REASON_REQUIRED", false, false,
            "El rechazo requiere un motivo de al menos 10 caracteres."),
        new("INV-09", "El depósito solo se acredita en un evento aprobado; una transacción se acredita una sola vez (BR-15).",
            "HallEvent.AccreditDeposit", "EVENT.DEPOSIT_NOT_ACCEPTED", false, false,
            "Solo se acredita el depósito en un evento aprobado por Dirección (estado actual: Requested)."),
        new("INV-10", "El monto acreditado nunca es negativo ni está en otra moneda.",
            "Money", "MONEY.CURRENCY_MISMATCH / MONEY.NEGATIVE_AMOUNT", false, false,
            "No se pueden combinar montos en monedas distintas (BOB y USD)."),
        new("INV-11", "El evento solo se inicia desde 1 h antes de su inicio y antes de su fin (BR-18).",
            "HallEvent.Start", "EVENT.START_OUTSIDE_WINDOW", false, false,
            "El evento solo puede iniciarse desde 1 h antes de su hora de inicio y antes de que termine."),
        new("INV-12", "Solo se reprograma un evento cancelado con saldo a favor, conservando duración, aforo, extras y total (BR-23).",
            "HallEvent.Reschedule", "EVENT.RESCHEDULE_NOT_ALLOWED", false, false,
            "Solo se puede reprogramar un evento cancelado que generó saldo a favor."),
        new("INV-13", "Los servicios extra solo cambian a través del evento y los equipos no superan el inventario (BR-08).",
            "HallEvent / Quote (extra services)", "EVENT.EXTRA_SERVICE_INVALID", false, false,
            "No hay suficiente 'Proyector': se pidieron 2 y el inventario es 1."),
        new("INV-14", "La cotización vale 96 horas; una vencida no se puede aceptar (BR-09).",
            "Quote", "QUOTE.EXPIRED", true, false,
            "La cotización venció el 01/11/2026 12:00 UTC y ya no se puede aceptar."),
        new("INV-15", "El precio y los servicios de una cotización aceptada quedan congelados: no existe método que los cambie.",
            "Quote", "(por diseño)", false, true,
            "No hay operación para cambiar el precio de una cotización aceptada."),
        new("INV-16", "El evento respeta el horario operativo con sus buffers y la anticipación de 3 a 180 días (BR-02, BR-05).",
            "Quote.Issue / BookingPolicy", "QUOTE.SLOT_OUTSIDE_OPERATING_HOURS / QUOTE.DATE_OUT_OF_RANGE", false, false,
            "El salón opera de 07:00 a 23:00 incluyendo 1 h de montaje y 1 h de limpieza."),
        new("INV-17", "El descuento de membresía está entre 0 % y 10 % y solo se aplica al alquiler del salón (BR-07).",
            "PriceBreakdown", "QUOTE.INVALID_DISCOUNT", true, false,
            "El descuento de membresía debe estar entre 0 % y 10 % (se indicó 15 %)."),
        new("INV-18", "Ningún bloqueo activo solapa a otro, contando 1 h de montaje y 1 h de limpieza (BR-03).",
            "HallSchedule", "SCHEDULE.SLOT_OVERLAP", true, false,
            "El salón no está disponible: la franja se solapa con otra reserva considerando 1 h de montaje y 1 h de limpieza."),
        new("INV-19", "Un evento tiene como máximo un bloqueo activo.",
            "HallSchedule", "SCHEDULE.DUPLICATE_BLOCK", true, false,
            "El evento ya tiene un bloqueo activo en el salón."),
        new("INV-20", "Los equipos asignados a un bloqueo no superan el inventario.",
            "AvailabilityChecker", "SCHEDULE.EQUIPMENT_UNAVAILABLE", false, false,
            "Equipo no disponible: se pidieron 2 de 'PROJECTOR' y el inventario es 1."),
        new("INV-21", "El saldo a favor nunca es negativo: aplicar más de lo disponible aplica solo lo disponible.",
            "CustomerCredit", "CREDIT.INSUFFICIENT", true, false,
            "El cliente no tiene saldo a favor disponible."),
        new("INV-22", "El saldo a favor no se convierte en dinero: no existe operación de retiro ni reembolso.",
            "CustomerCredit", "(por diseño)", false, true,
            "No hay operación para retirar o reembolsar el saldo a favor.")
    ];

    public static InvariantInfo? Find(string id) =>
        All.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The first (main) code of the invariant, without the second alternative.</summary>
    public static string FirstCode(InvariantInfo info) => info.Code.Split(" / ")[0];
}
