using System.Security.Cryptography;
using Nodo.BuildingBlocks.Application;
using Nodo.EventLifecycle.Contracts;
using Nodo.External.Simulated.Downstream;
using Nodo.External.Simulated.Payments;
using Nodo.SharedKernel;

namespace Nodo.External.Simulated.Handlers;

// Each downstream system translates our Published Language (integration events) into its own model. That translation
// is the ACL, and it belongs to the downstream context: that is why these handlers live here and not in the modules.
// The simulated systems have no inbox of ours, so every handler remembers the messages it already processed.

public static class Cents
{
    public static long Of(decimal amount) => (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
}

// ---- Pasarela de Pagos: a payment link for what the customer owes ----

internal sealed class PaymentLinkOnApprovedHandler(PaymentGatewayContext gateway, ProcessedMessages processed)
    : IIntegrationEventHandler<HallEventApprovedByManagementIntegrationEvent>
{
    public Task HandleAsync(HallEventApprovedByManagementIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        if (!processed.TryMark(e.Id, nameof(PaymentLinkOnApprovedHandler)))
            return Task.CompletedTask;

        gateway.CreateLink(
            e.EventId.ToString(), Cents.Of(e.DepositRequired),
            $"Depósito de garantía ({e.DepositPercentage} %) del evento {e.EventId.ToString()[..8]}", e.OccurredOnUtc);
        return Task.CompletedTask;
    }
}

/// <summary>After a reschedule that the credit did not fully cover, the customer pays only the difference.</summary>
internal sealed class PaymentLinkOnRescheduledHandler(PaymentGatewayContext gateway, ProcessedMessages processed)
    : IIntegrationEventHandler<HallEventRescheduledWithCreditIntegrationEvent>
{
    public Task HandleAsync(HallEventRescheduledWithCreditIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        if (e.DepositPending <= 0 || !processed.TryMark(e.Id, nameof(PaymentLinkOnRescheduledHandler)))
            return Task.CompletedTask;

        gateway.CreateLink(
            e.EventId.ToString(), Cents.Of(e.DepositPending),
            $"Diferencia del depósito tras reprogramar el evento {e.EventId.ToString()[..8]}", e.OccurredOnUtc);
        return Task.CompletedTask;
    }
}

// ---- Control de Acceso: ACL that turns a confirmed event into a temporary credential ----

/// <summary>Pure translation, kept apart from the handler so it is easy to verify.</summary>
public static class AccessCredentialTranslator
{
    /// <summary>Capacity = attendance; valid from one hour before the start (setup) until the end.</summary>
    public static AccessCredential FromConfirmed(HallEventFirmlyConfirmedIntegrationEvent e, string pin) => new(
        Guid.NewGuid(),
        e.EventId.ToString(),
        pin,
        $"NODO|EVT:{e.EventId:N}|PIN:{pin}",
        e.Attendance,
        NodoTimeZone.ToUtc(e.Date, e.Start).AddHours(-1),
        NodoTimeZone.ToUtc(e.Date, e.End),
        "ACTIVE",
        null);
}

internal sealed class AccessCredentialOnConfirmedHandler(AccessControlContext access, ProcessedMessages processed)
    : IIntegrationEventHandler<HallEventFirmlyConfirmedIntegrationEvent>
{
    public Task HandleAsync(HallEventFirmlyConfirmedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        if (!processed.TryMark(e.Id, nameof(AccessCredentialOnConfirmedHandler)))
            return Task.CompletedTask;

        // A rescheduled event is confirmed again on a new date: the previous credential must stop working.
        access.Credentials.Update(
            c => c.EventRef == e.EventId.ToString() && c.Status == "ACTIVE",
            c => c with { Status = "REVOKED", RevokedAtUtc = e.OccurredOnUtc });

        var pin = RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString();
        access.Credentials.Add(AccessCredentialTranslator.FromConfirmed(e, pin));
        return Task.CompletedTask;
    }
}

internal sealed class AccessCredentialRevokeOnCancelledHandler(AccessControlContext access, ProcessedMessages processed)
    : IIntegrationEventHandler<HallEventCancelledWithCreditIntegrationEvent>
{
    public Task HandleAsync(HallEventCancelledWithCreditIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        if (!processed.TryMark(e.Id, nameof(AccessCredentialRevokeOnCancelledHandler)))
            return Task.CompletedTask;

        access.Credentials.Update(
            c => c.EventRef == e.EventId.ToString() && c.Status == "ACTIVE",
            c => c with { Status = "REVOKED", RevokedAtUtc = e.OccurredOnUtc });
        return Task.CompletedTask;
    }
}

// ---- Facturación y Cobros: ledger ----

internal sealed class BillingOnConfirmedHandler(BillingContext billing, ProcessedMessages processed)
    : IIntegrationEventHandler<HallEventFirmlyConfirmedIntegrationEvent>
{
    public Task HandleAsync(HallEventFirmlyConfirmedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        if (!processed.TryMark(e.Id, nameof(BillingOnConfirmedHandler)))
            return Task.CompletedTask;

        billing.Ledger.Add(new LedgerEntry(
            Guid.NewGuid(), e.OccurredOnUtc, "DepositoRegistrado", e.CustomerId.ToString(), e.EventId.ToString(),
            e.DepositAccredited, "BOB", $"Depósito de garantía del evento confirmado (total {e.Total:0.00} BOB)."));
        return Task.CompletedTask;
    }
}

internal sealed class BillingOnCancelledHandler(BillingContext billing, ProcessedMessages processed)
    : IIntegrationEventHandler<HallEventCancelledWithCreditIntegrationEvent>
{
    public Task HandleAsync(HallEventCancelledWithCreditIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        if (e.CreditAmount <= 0 || !processed.TryMark(e.Id, nameof(BillingOnCancelledHandler)))
            return Task.CompletedTask;

        billing.Ledger.Add(new LedgerEntry(
            Guid.NewGuid(), e.OccurredOnUtc, "SaldoAFavorRegistrado", e.CustomerId.ToString(), e.EventId.ToString(),
            e.CreditAmount, "BOB", "Lo acreditado pasa íntegro a saldo a favor del cliente; no hay devolución de dinero."));
        return Task.CompletedTask;
    }
}

internal sealed class BillingOnCompletedHandler(BillingContext billing, ProcessedMessages processed)
    : IIntegrationEventHandler<HallEventCompletedIntegrationEvent>
{
    public Task HandleAsync(HallEventCompletedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        if (!processed.TryMark(e.Id, nameof(BillingOnCompletedHandler)))
            return Task.CompletedTask;

        billing.Ledger.Add(new LedgerEntry(
            Guid.NewGuid(), e.OccurredOnUtc, "LiquidacionFinalCompletada", e.CustomerId.ToString(), e.EventId.ToString(),
            e.RemainingToSettle, "BOB",
            $"Saldo restante liquidado: total {e.Total:0.00} BOB menos depósito {e.DepositAccredited:0.00} BOB."));
        return Task.CompletedTask;
    }
}

// ---- Proveedor de Catering ----

internal sealed class CateringOrderHandler(CateringContext catering, ProcessedMessages processed)
    : IIntegrationEventHandler<ExtraServicesAssignedIntegrationEvent>
{
    public Task HandleAsync(ExtraServicesAssignedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var lines = e.ExtraServices
            .Where(s => s.Type == "ExternalCatering")
            .Select(s => new CateringOrderLine(s.Code, s.Description, s.Quantity, s.UnitCost))
            .ToList();

        if (lines.Count == 0 || !processed.TryMark(e.Id, nameof(CateringOrderHandler)))
            return Task.CompletedTask;

        catering.Orders.Add(new CateringOrder(Guid.NewGuid(), e.EventId.ToString(), e.OccurredOnUtc, "RECEIVED", lines));
        return Task.CompletedTask;
    }
}
