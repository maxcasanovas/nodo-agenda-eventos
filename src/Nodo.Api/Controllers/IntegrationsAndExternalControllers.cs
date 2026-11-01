using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nodo.Api.Contracts;
using Nodo.BuildingBlocks.Application;
using Nodo.EventLifecycle.Infrastructure;
using Nodo.External.Simulated.Downstream;
using Nodo.External.Simulated.Handlers;
using Nodo.External.Simulated.Memberships;
using Nodo.External.Simulated.Payments;
using Nodo.External.Simulated.Spaces;
using Nodo.Api.OpenApi;

namespace Nodo.Api.Controllers;

[Route("api/integrations")]
[Tags("Integraciones (ACL)")]
public sealed class IntegrationsController(IPaymentGatewayAcl acl) : NodoControllerBase
{
    [HttpPost("payment-gateway/webhook")]
    [EndpointSummary("Webhook de la pasarela de pagos")]
    [EndpointDescription(
        "Recibe el payload **del proveedor** (`txn_id`, `amount_cents`, `currency_code`, `status`, `metadata.merchant_ref`) y lo traduce " +
        "a nuestro lenguaje: centavos a bolivianos, `merchant_ref` a identificador de evento y un pago `APPROVED` a la acreditación del depósito. " +
        "Un pago `DECLINED` se confirma pero **no acredita**. Reenviar el mismo `txn_id` no duplica la acreditación. " +
        "Un `merchant_ref` que no es un identificador responde 400.")]
    [Invariants("INV-09", "INV-10")]
    [ProducesResponseType<WebhookAck>(StatusCodes.Status200OK)]
    public async Task<ActionResult<WebhookAck>> PaymentGatewayWebhook(GatewayWebhookPayload payload, CancellationToken ct) =>
        Ok(await acl.ReceiveAsync(payload, ct));
}

/// <summary>Read-only windows into the simulated external systems, plus the one action a provider can take: pay.</summary>
[ApiController]
[Route("api/external")]
[Produces("application/json")]
[Tags("Sistemas externos (simulados)")]
public sealed class ExternalController(
    SpacesContext spaces,
    MembershipsContext memberships,
    PaymentGatewayContext gateway,
    AccessControlContext access,
    NotificationsContext notifications,
    BillingContext billing,
    CateringContext catering,
    IHttpClientFactory httpClientFactory,
    IClock clock) : ControllerBase
{
    public sealed record CatalogResponse(IReadOnlyList<VenueDto> Venues, IReadOnlyList<CateringItemDto> CateringMenu);

    public sealed record PayLinkResponse(string TxnId, int WebhookStatusCode, JsonElement? WebhookResponse, PaymentLink Link);

    [HttpGet("catalog")]
    [EndpointSummary("Espacios y Recursos: catálogo")]
    [EndpointDescription("El salón, su inventario de equipos con costo por evento y el menú del proveedor de catering, con el modelo propio de ese contexto.")]
    [ProducesResponseType<CatalogResponse>(StatusCodes.Status200OK)]
    public CatalogResponse Catalog() => new(spaces.Venues, spaces.CateringMenu);

    [HttpGet("customers")]
    [EndpointSummary("Membresías y Planes: directorio de clientes")]
    [EndpointDescription("Miembros con su plan, estado (`ACTIVE`, `EXPIRED`, `NONE`) y descuento. Nuestro módulo adopta el estado y el descuento tal como se publican.")]
    [ProducesResponseType<IReadOnlyList<MemberRecord>>(StatusCodes.Status200OK)]
    public IReadOnlyList<MemberRecord> Customers() => memberships.Members;

    [HttpGet("payment-links")]
    [EndpointSummary("Pasarela de Pagos: links de pago")]
    [EndpointDescription("Un link por cada aprobación (y por la diferencia tras una reprogramación no cubierta), con el historial de intentos de pago.")]
    [ProducesResponseType<IReadOnlyList<PaymentLink>>(StatusCodes.Status200OK)]
    public IReadOnlyList<PaymentLink> PaymentLinks() => gateway.Links;

    [HttpPost("payment-links/{id}/pay")]
    [EndpointSummary("Paga (o rechaza) un link, como lo haría el proveedor")]
    [EndpointDescription(
        "Construye el payload del proveedor y **llama al webhook por HTTP contra esta misma API**, de modo que el ACL se ejercita de verdad. " +
        "Sin `amount` paga lo que falta del link; con `decline: true` el proveedor envía un pago `DECLINED`. " +
        "Si el webhook responde con un error de regla (por ejemplo 422), se devuelve tal cual.")]
    [ProducesResponseType<PayLinkResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Pay(string id, PayLinkRequest? request, CancellationToken ct)
    {
        var link = gateway.Find(id) ?? throw new NotFoundException($"No existe el link de pago '{id}'.");
        request ??= new PayLinkRequest();

        var owed = Math.Max(link.AmountCents - link.PaidCents, 0);
        var cents = request.Amount is { } amount ? Cents.Of(amount) : (owed > 0 ? owed : link.AmountCents);
        var txnId = $"TXN-{Guid.NewGuid():N}"[..16].ToUpperInvariant();
        var payload = new GatewayWebhookPayload
        {
            TxnId = txnId,
            AmountCents = cents,
            CurrencyCode = link.CurrencyCode,
            Status = request.Decline ? "DECLINED" : "APPROVED",
            Metadata = new GatewayMetadata { MerchantRef = link.MerchantRef }
        };

        var response = await httpClientFactory.CreateClient("self")
            .PostAsJsonAsync("/api/integrations/payment-gateway/webhook", payload, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        // The provider decides on its side whether the money moved, whatever our system answers afterwards.
        gateway.RecordAttempt(link, txnId, cents, approved: !request.Decline, clock.UtcNow);

        if (!response.IsSuccessStatusCode)
        {
            return new ContentResult
            {
                StatusCode = (int)response.StatusCode,
                ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json",
                Content = body
            };
        }

        return Ok(new PayLinkResponse(txnId, (int)response.StatusCode, JsonSerializer.Deserialize<JsonElement>(body), link));
    }

    [HttpGet("access-credentials")]
    [EndpointSummary("Control de Acceso: credenciales")]
    [EndpointDescription("PIN de 6 dígitos y texto del QR, con capacidad igual al aforo y vigencia desde 1 h antes del inicio hasta el fin. Se revocan al cancelar.")]
    [ProducesResponseType<IReadOnlyList<AccessCredential>>(StatusCodes.Status200OK)]
    public IReadOnlyList<AccessCredential> AccessCredentials() => access.Credentials.Items.OrderByDescending(c => c.ValidFromUtc).ToList();

    [HttpGet("notifications")]
    [EndpointSummary("Notificaciones: feed de mensajes")]
    [EndpointDescription("Un mensaje legible en español por cada evento de integración publicado, los más recientes primero.")]
    [ProducesResponseType<IReadOnlyList<Notification>>(StatusCodes.Status200OK)]
    public IReadOnlyList<Notification> Notifications() => notifications.Feed.Items.OrderByDescending(n => n.SentAt).ToList();

    [HttpGet("billing/ledger")]
    [EndpointSummary("Facturación y Cobros: ledger")]
    [EndpointDescription("Depósitos registrados, saldos a favor y la `LiquidacionFinalCompletada` de cada evento cerrado.")]
    [ProducesResponseType<IReadOnlyList<LedgerEntry>>(StatusCodes.Status200OK)]
    public IReadOnlyList<LedgerEntry> Ledger() => billing.Ledger.Items.OrderByDescending(e => e.AtUtc).ToList();

    [HttpGet("catering-orders")]
    [EndpointSummary("Proveedor de Catering: órdenes recibidas")]
    [EndpointDescription("Una orden por cada evento confirmado que incluye catering, con la cantidad por persona.")]
    [ProducesResponseType<IReadOnlyList<CateringOrder>>(StatusCodes.Status200OK)]
    public IReadOnlyList<CateringOrder> CateringOrders() => catering.Orders.Items.OrderByDescending(o => o.ReceivedAtUtc).ToList();
}
