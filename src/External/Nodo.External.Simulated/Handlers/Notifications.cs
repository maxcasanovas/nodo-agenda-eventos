using System.Globalization;
using Nodo.BuildingBlocks.Application;
using Nodo.EventLifecycle.Contracts;
using Nodo.External.Simulated.Downstream;
using Nodo.External.Simulated.Memberships;
using Nodo.HallSchedule.Contracts;
using Nodo.Quotes.Contracts;

namespace Nodo.External.Simulated.Handlers;

public sealed record NotificationMessage(string To, string Channel, string Subject, string Body);

/// <summary>Turns any integration event into a readable Spanish message. Unknown events get a generic line, never an error.</summary>
public sealed class NotificationFormatter(MembershipsContext memberships, EventDirectory events)
{
    private const string Management = "Dirección";
    private const string Operations = "Operaciones";
    private const string Reception = "Recepción";
    private const string Billing = "Facturación";

    public NotificationMessage Format(IIntegrationEvent integrationEvent) => integrationEvent switch
    {
        // ---- Quotes
        QuoteIssuedIntegrationEvent e => ToCustomer(e.CustomerId, "Cotización emitida",
            $"Tu cotización para el {Day(e.Date)} ({e.Start:HH\\:mm}-{e.End:HH\\:mm}) es de {Bs(e.Total)}. " +
            $"Vale hasta el {Local(e.ExpiresAtUtc)}."),
        QuoteAcceptedIntegrationEvent e => ToCustomer(e.CustomerId, "Cotización aceptada",
            $"Aceptaste la cotización de {Bs(e.Total)} para el {Day(e.Date)}. Registramos tu solicitud para Dirección."),
        QuoteExpiredIntegrationEvent e => ToCustomer(e.CustomerId, "Cotización vencida",
            "Tu cotización venció (4 días sin aceptarse). Puedes pedir una nueva."),

        // ---- EventLifecycle
        EventRequestCreatedIntegrationEvent e => New(Management, "panel", "Nueva solicitud de evento",
            $"{Management}: nueva solicitud de {RememberCustomer(e.EventId, e.CustomerId)} para el {Day(e.Date)} " +
            $"({e.Start:HH\\:mm}-{e.End:HH\\:mm}, {e.Attendance} personas). Debes responder hoy."),
        HallEventApprovedByManagementIntegrationEvent e => New(Of(e.EventId), "email", "Solicitud aprobada",
            $"{Subject(e.EventId)}: {e.ApprovedBy} aprobó la solicitud. Deposita {Bs(e.DepositRequired)} " +
            $"({e.DepositPercentage} %) antes del {Local(e.DepositDeadlineUtc)} para confirmarla."),
        EventRequestRejectedIntegrationEvent e => New(Of(e.EventId), "email", "Solicitud rechazada",
            $"{Subject(e.EventId)}: la solicitud para el {Day(e.Date)} fue rechazada por {e.RejectedBy}. Motivo: {e.Reason}"),
        GuaranteeDepositAccreditedIntegrationEvent e => New(Of(e.EventId), "email", "Pago recibido",
            $"{Subject(e.EventId)}: recibimos {Bs(e.Amount)}. Acumulado: {Bs(e.AccreditedTotal)}" +
            (e.IsCovered ? ". El depósito quedó cubierto." : ". Aún falta completar el depósito.")),
        HallEventFirmlyConfirmedIntegrationEvent e => ToCustomer(e.CustomerId, "Evento confirmado en firme",
            $"Tu evento del {Day(e.Date)} ({e.Start:HH\\:mm}-{e.End:HH\\:mm}) está confirmado. " +
            "Te generamos una credencial de acceso (PIN y QR)."),
        ExtraServicesAssignedIntegrationEvent e => New(Operations, "panel", "Servicios extra asignados",
            $"{Operations}: se asignaron {e.ExtraServices.Count} servicio(s) extra al {Subject(e.EventId).ToLowerInvariant()}."),
        HallEventStartedIntegrationEvent e => New(Reception, "panel", "Evento iniciado",
            $"{Subject(e.EventId)} fue iniciado por {e.StartedBy}."),
        HallEventCompletedIntegrationEvent e => ToCustomer(e.CustomerId, "Evento completado",
            $"Gracias por elegirnos. Saldo a liquidar: {Bs(e.RemainingToSettle)} (total {Bs(e.Total)}, depósito {Bs(e.DepositAccredited)})."),
        HallEventCancelledWithCreditIntegrationEvent e => ToCustomer(e.CustomerId, "Evento cancelado",
            e.CreditAmount > 0
                ? $"Cancelaste el evento del {Day(e.Date)}. Lo pagado ({Bs(e.CreditAmount)}) queda como saldo a favor; no hay devolución en dinero."
                : $"Cancelaste el evento del {Day(e.Date)}. No tenías pagos acreditados."),
        HallEventRescheduledWithCreditIntegrationEvent e => ToCustomer(e.CustomerId, "Evento reprogramado",
            $"Tu evento pasó al {Day(e.NewDate)} ({e.NewStart:HH\\:mm}-{e.NewEnd:HH\\:mm}) usando {Bs(e.CreditApplied)} de saldo a favor. " +
            (e.ResultingStatus == "Confirmed"
                ? "Quedó confirmado en firme."
                : $"Falta pagar {Bs(e.DepositPending)} para confirmarlo.")),
        HallEventExpiredIntegrationEvent e => ToCustomer(e.CustomerId, "Solicitud vencida",
            $"La solicitud para el {Day(e.Date)} venció sin completarse." +
            (e.CreditAmount > 0 ? $" Lo que pagaste ({Bs(e.CreditAmount)}) pasó a saldo a favor." : string.Empty)),
        CustomerCreditGrantedIntegrationEvent e => ToCustomer(e.CustomerId, "Saldo a favor",
            $"Se acreditaron {Bs(e.Amount)} a tu saldo a favor. Saldo actual: {Bs(e.Balance)}. Sólo se usa para reprogramar."),
        CustomerCreditAppliedIntegrationEvent e => ToCustomer(e.CustomerId, "Saldo a favor aplicado",
            $"Se aplicaron {Bs(e.Amount)} de tu saldo a favor. Saldo actual: {Bs(e.Balance)}."),

        // ---- HallSchedule
        SlotProvisionallyBlockedIntegrationEvent e => New(Operations, "panel", "Salón bloqueado (provisional)",
            $"Salón bloqueado provisionalmente el {Day(e.Date)} de {e.Start:HH\\:mm} a {e.End:HH\\:mm} para el {Subject(e.EventId).ToLowerInvariant()}."),
        SlotBlockConsolidatedIntegrationEvent e => New(Operations, "panel", "Bloqueo del salón consolidado",
            $"El bloqueo del {Day(e.Date)} ({e.Start:HH\\:mm}-{e.End:HH\\:mm}) ya es definitivo."),
        SlotReleasedIntegrationEvent e => New(Operations, "panel", "Salón liberado",
            $"Se liberó el salón del {Day(e.Date)} ({e.Start:HH\\:mm}-{e.End:HH\\:mm}); ya puede reservarse de nuevo."),

        _ => New(Billing, "panel", integrationEvent.GetType().Name, $"Evento publicado: {integrationEvent.GetType().Name}.")
    };

    private NotificationMessage ToCustomer(Guid customerId, string subject, string body) =>
        New($"Cliente: {NameOf(customerId)}", "email", subject, body);

    private static NotificationMessage New(string to, string channel, string subject, string body) =>
        new(to, channel, subject, body);

    private string Of(Guid eventId) => events.CustomerOf(eventId) is { } name ? $"Cliente: {name}" : "Cliente";

    private string Subject(Guid eventId) => $"Evento {eventId.ToString()[..8]}";

    private string NameOf(Guid customerId) => memberships.Find(customerId.ToString())?.FullName ?? "cliente";

    private string RememberCustomer(Guid eventId, Guid customerId)
    {
        var name = NameOf(customerId);
        events.Remember(eventId, name);
        return name;
    }

    private static string Bs(decimal amount) => $"Bs {amount.ToString("N2", CultureInfo.InvariantCulture)}";

    private static string Day(DateOnly date) => date.ToString("dd/MM", CultureInfo.InvariantCulture);

    private static string Local(DateTime utc) =>
        Nodo.SharedKernel.NodoTimeZone.ToLocal(utc).ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);
}

/// <summary>Registered once per integration event contract: every event published anywhere becomes a readable notification.</summary>
internal sealed class NotificationHandler<TEvent>(
    NotificationsContext notifications, NotificationFormatter formatter, ProcessedMessages processed)
    : IIntegrationEventHandler<TEvent>
    where TEvent : IIntegrationEvent
{
    public Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (!processed.TryMark(integrationEvent.Id, nameof(NotificationHandler<TEvent>)))
            return Task.CompletedTask;

        var message = formatter.Format(integrationEvent);
        notifications.Feed.Add(new Notification(
            Guid.NewGuid(), message.To, message.Channel, message.Subject, message.Body, integrationEvent.OccurredOnUtc));
        return Task.CompletedTask;
    }
}
