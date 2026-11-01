using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Nodo.Api.Contracts;
using Nodo.Api.Controllers;
using Nodo.Api.Invariants;
using Nodo.BuildingBlocks.Application;
using Nodo.BuildingBlocks.Infrastructure.Messaging;
using Nodo.EventLifecycle.Application;
using Nodo.EventLifecycle.Infrastructure;
using Nodo.External.Simulated.Downstream;
using Nodo.External.Simulated.Memberships;
using Nodo.External.Simulated.Payments;
using Nodo.HallSchedule.Application;
using Nodo.Quotes.Application;

namespace Nodo.Api.OpenApi;

/// <summary>
/// OpenAPI document of the API. Beyond what ASP.NET generates it adds, per endpoint, the list of invariants it can
/// violate and a ProblemDetails example for each error status (the 422 one carrying the real code), and an example for
/// every request body and the main responses, so Scalar shows something a caller can send as is.
/// </summary>
public static class NodoOpenApi
{
    private static readonly (string Name, string Description)[] Tags =
    [
        ("Cotizaciones", "Presupuestos con tarifa degresiva y vigencia de 96 horas. Emitir no bloquea el calendario; aceptar sí exige disponibilidad."),
        ("Ciclo del Evento", "Solicitud, decisión de Dirección, depósito de garantía, confirmación, ejecución, cancelación con saldo a favor y reprogramación."),
        ("Agenda del Salón", "Calendario de ocupación con buffers de montaje y limpieza, y consulta de disponibilidad."),
        ("Integraciones (ACL)", "Puntos de entrada de sistemas externos. Su payload se traduce a nuestro lenguaje en una capa anticorrupción."),
        ("Sistemas externos (simulados)", "Ventanas a los contextos externos hardcodeados: espacios, membresías, pasarela, facturación, acceso, notificaciones y catering."),
        ("Demo", "Reloj virtual, jobs de expiración, registro de eventos de integración y laboratorio de invariantes.")
    ];

    public static IServiceCollection AddNodoOpenApi(this IServiceCollection services) => services.AddOpenApi(options =>
    {
        options.AddDocumentTransformer(TransformDocument);
        options.AddOperationTransformer(TransformOperation);
        options.AddSchemaTransformer(TransformSchema);
    });

    // ---- document ----------------------------------------------------------------------------------------------------

    private static Task TransformDocument(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Nodo · Agenda y Eventos API",
            Version = "v1",
            Description =
                "Prototipo del subdominio **Agenda y Eventos** del coworking Nodo.\n\n" +
                "- Los errores de regla de negocio responden **422** con un `code` estable (`EVENT.…`, `QUOTE.…`, `SCHEDULE.…`, `CREDIT.…`, `MONEY.…`) y un mensaje en español.\n" +
                "- La validación de forma responde **400**; un recurso inexistente **404**; la concurrencia o una restricción de la base de datos **409**.\n" +
                "- `GET /api/demo/invariants` lista las 22 invariantes y `POST /api/demo/invariants/{id}/probe` intenta violar cada una a través del caso de uso real.\n" +
                "- No hay autenticación: el rol se elige en el cliente y cada comando recibe quién lo ejecuta."
        };

        document.Tags = new HashSet<OpenApiTag>(Tags.Select(t => new OpenApiTag { Name = t.Name, Description = t.Description }));
        return Task.CompletedTask;
    }

    // ---- operations --------------------------------------------------------------------------------------------------

    private static Task TransformOperation(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        var isRead = string.Equals(context.Description.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase);
        var hasRouteId = context.Description.ParameterDescriptions.Any(p => p.Source.Id == "Path");
        var invariants = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<InvariantsAttribute>()
            .SelectMany(a => a.Ids)
            .Select(InvariantCatalog.Find)
            .OfType<InvariantInfo>()
            .ToList();

        if (invariants.Count > 0)
            operation.Description = AppendInvariants(operation.Description, invariants);

        if (operation.Responses is null)
            return Task.CompletedTask;

        // Reads cannot conflict or break a rule, and only lookups by id can miss: do not advertise what cannot happen.
        if (isRead)
        {
            operation.Responses.Remove("409");
            operation.Responses.Remove("422");
        }

        if (!hasRouteId)
            operation.Responses.Remove("404");

        SetProblemExample(operation, "400", ProblemExamples.Validation);
        SetProblemExample(operation, "404", ProblemExamples.NotFound);
        SetProblemExample(operation, "409", ProblemExamples.Conflict);
        SetProblemExample(operation, "422", invariants.Count > 0 ? ProblemExamples.Rule(invariants[0]) : ProblemExamples.GenericRule);
        return Task.CompletedTask;
    }

    private static string AppendInvariants(string? description, IReadOnlyList<InvariantInfo> invariants)
    {
        var text = new StringBuilder(description);
        text.Append("\n\n**Invariantes que puede violar** (respuesta `422` con su `code`):\n\n");
        foreach (var invariant in invariants)
        {
            text.Append($"- `{invariant.Id}` → `{invariant.Code}`: {invariant.Description}\n");
        }

        return text.ToString();
    }

    private static void SetProblemExample(OpenApiOperation operation, string status, JsonNode example)
    {
        if (operation.Responses is null || !operation.Responses.TryGetValue(status, out var response) || response.Content is null)
            return;

        foreach (var media in response.Content.Values)
            media.Example = example.DeepClone();
    }

    // ---- schemas (examples) ------------------------------------------------------------------------------------------

    private static Task TransformSchema(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken ct)
    {
        var type = context.JsonTypeInfo.Type;
        if (type.IsGenericType || !Examples.IsDocumented(type))
            return Task.CompletedTask;

        using var scope = context.ApplicationServices.CreateScope();
        var today = scope.ServiceProvider.GetRequiredService<IClock>().TodayLocal;
        if (Examples.For(type, today) is { } example)
            schema.Examples = [example]; // OpenAPI 3.1: a schema carries a list of examples

        return Task.CompletedTask;
    }
}

internal static class ProblemExamples
{
    private static JsonNode Problem(int status, string title, string detail, string? code = null, JsonObject? errors = null)
    {
        var problem = new JsonObject
        {
            ["type"] = $"https://tools.ietf.org/html/rfc9110#section-15.5.{status switch { 400 => 1, 404 => 5, 409 => 10, _ => 21 }}",
            ["title"] = title,
            ["status"] = status,
            ["detail"] = detail
        };
        if (code is not null)
            problem["code"] = code;
        if (errors is not null)
            problem["errors"] = errors;
        problem["traceId"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-00";
        return problem;
    }

    public static JsonNode Validation => Problem(400, "Solicitud inválida", "Uno o más campos no son válidos.",
        errors: new JsonObject { ["ApprovedBy"] = new JsonArray("Indica quién aprueba.") });

    public static JsonNode NotFound => Problem(404, "No encontrado", "No existe el evento '44444444-0000-0000-0000-000000000001'.");

    public static JsonNode Conflict => Problem(409, "Conflicto", "Otra operación modificó este registro al mismo tiempo. Vuelve a intentarlo.");

    public static JsonNode Rule(InvariantInfo invariant) =>
        Problem(422, "Regla de dominio violada", invariant.ExampleMessage, InvariantCatalog.FirstCode(invariant));

    public static JsonNode GenericRule => Problem(422, "Regla de dominio violada",
        "No se puede aprobar un evento en estado ApprovedByManagement.", "EVENT.INVALID_STATE_TRANSITION");
}

/// <summary>Realistic example bodies, keyed by the request/response type they document.</summary>
internal static class Examples
{
    private const string Ana = "22222222-0000-0000-0000-000000000001";
    private const string TechSol = "22222222-0000-0000-0000-000000000002";
    private const string Event = "44444444-0000-0000-0000-000000000003";

    private static readonly HashSet<Type> Documented =
    [
        typeof(PreviewQuoteRequest), typeof(IssueQuoteRequest), typeof(AcceptQuoteRequest), typeof(ApproveRequest),
        typeof(RejectRequest), typeof(PerformedByRequest), typeof(CancelRequest), typeof(RescheduleRequest),
        typeof(PayLinkRequest), typeof(GatewayWebhookPayload), typeof(ExtraServiceRequest),
        typeof(QuotePreviewDto), typeof(IssuedQuoteDto), typeof(AcceptedQuoteDto), typeof(HallEventStatusDto),
        typeof(RescheduleResultDto), typeof(AccreditDepositResultDto), typeof(WebhookAck), typeof(AvailabilityDto),
        typeof(CustomerCreditDto), typeof(ProbeResult), typeof(PriceBreakdownDto),
        typeof(QuoteDto), typeof(HallEventDto), typeof(HallEventListItemDto), typeof(CalendarBlockDto),
        typeof(DemoController.AdvanceClockRequest), typeof(DemoController.ClockResponse), typeof(DemoController.JobResult),
        typeof(DemoInvariantsController.InvariantEntry), typeof(IntegrationEventLogEntry),
        typeof(ExternalController.CatalogResponse), typeof(ExternalController.PayLinkResponse), typeof(MemberRecord),
        typeof(PaymentLink), typeof(AccessCredential), typeof(Notification), typeof(LedgerEntry), typeof(CateringOrder)
    ];

    public static bool IsDocumented(Type type) => Documented.Contains(type);

    public static JsonNode? For(Type type, DateOnly today)
    {
        var day = today.AddDays(20).ToString("yyyy-MM-dd");
        var price = JsonNode.Parse(
            """{ "baseHourlyRate": 500, "hours": 4, "rateFactor": 0.90, "hallRental": 1800, "membershipDiscountPercentage": 5, "membershipDiscount": 90, "extrasTotal": 150, "total": 1860 }""");

        return type.Name switch
        {
            nameof(ExtraServiceRequest) => JsonNode.Parse("""{ "code": "PROJECTOR", "quantity": 1 }"""),
            nameof(PreviewQuoteRequest) => JsonNode.Parse(
                $$"""{ "customerId": "{{Ana}}", "date": "{{day}}", "startTime": "09:00:00", "endTime": "13:00:00", "attendance": 40, "extraServices": [ { "code": "PROJECTOR", "quantity": 1 }, { "code": "COFFEE_BREAK" } ] }"""),
            nameof(IssueQuoteRequest) => JsonNode.Parse(
                $$"""{ "customerId": "{{Ana}}", "date": "{{day}}", "startTime": "09:00:00", "endTime": "13:00:00", "attendance": 40, "extraServices": [ { "code": "PROJECTOR", "quantity": 1 } ], "performedBy": "Recepción Nodo" }"""),
            nameof(AcceptQuoteRequest) => JsonNode.Parse("""{ "acceptedBy": "Ana Gutiérrez" }"""),
            nameof(ApproveRequest) => JsonNode.Parse("""{ "depositPercentage": 30, "approvedBy": "Lucía Paz" }"""),
            nameof(RejectRequest) => JsonNode.Parse("""{ "reason": "El salón estará en mantenimiento esa fecha.", "rejectedBy": "Lucía Paz" }"""),
            nameof(PerformedByRequest) => JsonNode.Parse("""{ "performedBy": "Host de turno" }"""),
            nameof(CancelRequest) => JsonNode.Parse("""{ "reason": "El cliente cambió de planes.", "cancelledBy": "Recepción Nodo" }"""),
            nameof(RescheduleRequest) => JsonNode.Parse($$"""{ "newDate": "{{today.AddDays(40):yyyy-MM-dd}}", "newStartTime": "10:00:00", "performedBy": "Recepción Nodo" }"""),
            nameof(PayLinkRequest) => JsonNode.Parse("""{ "amount": 200, "decline": false }"""),
            nameof(GatewayWebhookPayload) => JsonNode.Parse(
                $$"""{ "txn_id": "TXN-9F3A2C71B0D4", "amount_cents": 20000, "currency_code": "BOB", "status": "APPROVED", "metadata": { "merchant_ref": "{{Event}}" } }"""),

            nameof(PriceBreakdownDto) => price,
            nameof(QuotePreviewDto) => JsonNode.Parse(
                $$"""{ "price": {{price}}, "extraServices": [ { "type": "OwnEquipment", "code": "PROJECTOR", "name": "Proyector", "quantity": 1, "unitCost": 150, "lineTotal": 150 } ], "availability": { "isAvailable": true, "code": null, "reason": null } }"""),
            nameof(IssuedQuoteDto) => JsonNode.Parse(
                $$"""{ "quoteId": "33333333-0000-0000-0000-000000000001", "issuedAtUtc": "{{today:yyyy-MM-dd}}T14:00:00Z", "expiresAtUtc": "{{today.AddDays(4):yyyy-MM-dd}}T14:00:00Z", "price": {{price}}, "extraServices": [ { "type": "OwnEquipment", "code": "PROJECTOR", "name": "Proyector", "quantity": 1, "unitCost": 150, "lineTotal": 150 } ], "availability": { "isAvailable": true, "code": null, "reason": null } }"""),
            nameof(AcceptedQuoteDto) => JsonNode.Parse(
                $$"""{ "quoteId": "33333333-0000-0000-0000-000000000001", "status": "Accepted", "acceptedAtUtc": "{{today:yyyy-MM-dd}}T15:30:00Z" }"""),
            nameof(HallEventStatusDto) => JsonNode.Parse($$"""{ "eventId": "{{Event}}", "status": "ApprovedByManagement" }"""),
            nameof(RescheduleResultDto) => JsonNode.Parse(
                $$"""{ "eventId": "{{Event}}", "status": "ApprovedByManagement", "creditApplied": 400, "depositPending": 200, "depositDeadlineUtc": "{{today.AddDays(2):yyyy-MM-dd}}T14:00:00Z" }"""),
            nameof(AccreditDepositResultDto) => JsonNode.Parse(
                $$"""{ "eventId": "{{Event}}", "status": "Confirmed", "accreditedTotal": 617, "pending": 0, "isCovered": true, "excessGrantedAsCredit": 0 }"""),
            nameof(WebhookAck) => JsonNode.Parse(
                $$"""{ "accredited": true, "outcome": "ACCREDITED_AND_COVERED", "eventId": "{{Event}}", "eventStatus": "Confirmed", "accreditedTotal": 617, "excessGrantedAsCredit": 0 }"""),
            nameof(AvailabilityDto) => JsonNode.Parse(
                """{ "isAvailable": false, "code": "SCHEDULE.SLOT_OVERLAP", "reason": "El salón no está disponible: la franja 15:00-17:00 se solapa con otra reserva (14:00-18:00) considerando 1 h de montaje y 1 h de limpieza." }"""),
            nameof(CustomerCreditDto) => JsonNode.Parse(
                $$"""{ "customerId": "{{TechSol}}", "customerName": "TechSol S.R.L.", "balance": 600, "movements": [ { "type": "Granted", "amount": 600, "eventId": "44444444-0000-0000-0000-000000000005", "reason": "Cancelación del evento", "occurredAtUtc": "{{today.AddDays(-8):yyyy-MM-dd}}T15:00:00Z" } ] }"""),
            nameof(ProbeResult) => JsonNode.Parse(
                """{ "invariantId": "INV-02", "attempted": true, "attempt": "Aprobar la solicitud con un depósito del 19 %", "rejected": true, "code": "EVENT.DEPOSIT_PERCENTAGE_OUT_OF_RANGE", "message": "El depósito de garantía debe ser entre 20 % y 50 % del total (se indicó 19 %).", "statusCode": 422 }"""),
            // ---- read models
            nameof(QuoteDto) => JsonNode.Parse(
                $$"""{ "id": "33333333-0000-0000-0000-000000000001", "customerId": "{{Ana}}", "customerName": "Ana Gutiérrez", "hallId": "11111111-1111-1111-1111-111111111111", "date": "{{day}}", "start": "09:00:00", "end": "13:00:00", "attendance": 40, "status": "Issued", "issuedAtUtc": "{{today:yyyy-MM-dd}}T14:00:00Z", "expiresAtUtc": "{{today.AddDays(4):yyyy-MM-dd}}T14:00:00Z", "acceptedAtUtc": null, "hoursRemaining": 96, "price": {{price}}, "extraServices": [ { "type": "OwnEquipment", "code": "PROJECTOR", "name": "Proyector", "quantity": 1, "unitCost": 150, "lineTotal": 150 } ] }"""),
            nameof(HallEventListItemDto) => JsonNode.Parse(
                $$"""{ "id": "{{Event}}", "customerId": "{{Ana}}", "customerName": "Ana Gutiérrez", "date": "{{day}}", "start": "10:00:00", "end": "14:00:00", "attendance": 35, "total": 3085, "status": "Confirmed", "decisionDeadlineUtc": "{{today:yyyy-MM-dd}}T04:00:00Z", "depositDeadlineUtc": null, "depositRequired": 617, "depositAccredited": 617 }"""),
            nameof(HallEventDto) => JsonNode.Parse(
                $$"""{ "id": "{{Event}}", "quoteId": "33333333-0000-0000-0000-000000000005", "customerId": "{{Ana}}", "customerName": "Ana Gutiérrez", "hallId": "11111111-1111-1111-1111-111111111111", "date": "{{day}}", "start": "10:00:00", "end": "14:00:00", "attendance": 35, "total": 3085, "status": "Confirmed", "deposit": { "percentage": 20, "required": 617, "accredited": 617, "pending": 0, "isCovered": true, "transactionIds": [ "TXN-9F3A2C71B0D4" ] }, "requestedAtUtc": "{{today:yyyy-MM-dd}}T12:00:00Z", "decisionDeadlineUtc": "{{today.AddDays(1):yyyy-MM-dd}}T04:00:00Z", "depositDeadlineUtc": null, "decision": { "decidedBy": "Lucía Paz", "decidedAtUtc": "{{today:yyyy-MM-dd}}T13:00:00Z", "approved": true, "rejectionReason": null }, "creditGenerated": 0, "rescheduleCount": 0, "extraServicesAssigned": true, "remainingToSettle": null, "extraServices": [ { "type": "OwnEquipment", "code": "PROJECTOR", "description": "Proyector", "quantity": 1, "unitCost": 150, "lineTotal": 150 } ], "history": [ { "status": "Requested", "occurredAtUtc": "{{today:yyyy-MM-dd}}T12:00:00Z", "actor": "Cliente", "note": "Solicitud creada a partir de una cotización aceptada." }, { "status": "ApprovedByManagement", "occurredAtUtc": "{{today:yyyy-MM-dd}}T13:00:00Z", "actor": "Lucía Paz", "note": "Aprobado con depósito de 20 % (617.00 BOB)." }, { "status": "Confirmed", "occurredAtUtc": "{{today:yyyy-MM-dd}}T14:00:00Z", "actor": "Sistema", "note": "Depósito cubierto: evento confirmado en firme." } ] }"""),
            nameof(CalendarBlockDto) => JsonNode.Parse(
                $$"""{ "blockId": "66666666-0000-0000-0000-000000000003", "eventId": "{{Event}}", "date": "{{day}}", "start": "10:00:00", "end": "14:00:00", "bufferStart": "09:00:00", "bufferEnd": "15:00:00", "kind": "Definitive", "equipment": [ { "code": "PROJECTOR", "quantity": 1 } ], "customerId": "{{Ana}}", "customerName": "Ana Gutiérrez", "eventStatus": "Confirmed" }"""),

            // ---- demo
            nameof(DemoController.AdvanceClockRequest) => JsonNode.Parse("""{ "days": 5, "hours": 0 }"""),
            nameof(DemoController.ClockResponse) => JsonNode.Parse(
                $$"""{ "utcNow": "{{today.AddDays(5):yyyy-MM-dd}}T14:00:00Z", "localNow": "{{today.AddDays(5):yyyy-MM-dd}}T10:00:00", "todayLocal": "{{today.AddDays(5):yyyy-MM-dd}}", "offsetHours": 120 }"""),
            nameof(DemoController.JobResult) => JsonNode.Parse("""{ "job": "QuoteExpirationJob", "changed": 1 }"""),
            nameof(DemoInvariantsController.InvariantEntry) => JsonNode.Parse(
                """{ "id": "INV-02", "description": "El depósito de garantía exigido está entre el 20 % y el 50 % del total (BR-14).", "owner": "GuaranteeDeposit", "code": "EVENT.DEPOSIT_PERCENTAGE_OUT_OF_RANGE", "databaseEnforced": true, "byDesign": false, "hasProbe": true }"""),
            nameof(IntegrationEventLogEntry) => JsonNode.Parse(
                $$"""{ "module": "event_lifecycle", "id": "0b8c5a4e-6f43-4a53-9c1e-7d6b2a1f0e11", "type": "HallEventFirmlyConfirmedIntegrationEvent", "content": { "eventId": "{{Event}}", "total": 3085 }, "occurredOnUtc": "{{today:yyyy-MM-dd}}T14:00:00Z", "processedOnUtc": "{{today:yyyy-MM-dd}}T14:00:01Z", "attempts": 0, "error": null }"""),

            // ---- simulated external systems (their own models, not ours)
            nameof(ExternalController.CatalogResponse) => JsonNode.Parse(
                """{ "venues": [ { "venue_uid": "11111111-1111-1111-1111-111111111111", "venue_code": "SALON-POLIVALENTE", "display_name": "Salón Polivalente", "max_capacity": 80, "opens_at": "07:00", "closes_at": "23:00", "assets": [ { "asset_code": "PROJECTOR", "label": "Proyector", "units_in_stock": 1, "fee_per_event": 150 } ] } ], "cateringMenu": [ { "sku": "COFFEE_BREAK", "title": "Coffee break", "price_per_head": 35 } ] }"""),
            nameof(MemberRecord) => JsonNode.Parse(
                $$"""{ "memberId": "{{Ana}}", "fullName": "Ana Gutiérrez", "kind": "Freelancer", "plan": "Flexible", "status": "ACTIVE", "discountPct": 5 }"""),
            nameof(PaymentLink) => JsonNode.Parse(
                $$"""{ "linkId": "LNK-3F9A2C71B0", "merchantRef": "{{Event}}", "amountCents": 61700, "currencyCode": "BOB", "description": "Depósito de garantía (20 %) del evento 44444444", "createdAtUtc": "{{today:yyyy-MM-dd}}T13:00:00Z", "status": "PARTIALLY_PAID", "paidCents": 20000, "attempts": [ { "txnId": "TXN-9F3A2C71B0D4", "amountCents": 20000, "status": "APPROVED", "atUtc": "{{today:yyyy-MM-dd}}T13:05:00Z" } ] }"""),
            nameof(ExternalController.PayLinkResponse) => JsonNode.Parse(
                $$"""{ "txnId": "TXN-9F3A2C71B0D4", "webhookStatusCode": 200, "webhookResponse": { "accredited": true, "outcome": "ACCREDITED", "eventId": "{{Event}}", "eventStatus": "ApprovedByManagement", "accreditedTotal": 200, "excessGrantedAsCredit": 0 }, "link": { "linkId": "LNK-3F9A2C71B0", "merchantRef": "{{Event}}", "amountCents": 61700, "currencyCode": "BOB", "description": "Depósito de garantía (20 %) del evento 44444444", "createdAtUtc": "{{today:yyyy-MM-dd}}T13:00:00Z", "status": "PARTIALLY_PAID", "paidCents": 20000, "attempts": [ { "txnId": "TXN-9F3A2C71B0D4", "amountCents": 20000, "status": "APPROVED", "atUtc": "{{today:yyyy-MM-dd}}T13:05:00Z" } ] } }"""),
            nameof(AccessCredential) => JsonNode.Parse(
                $$"""{ "credentialId": "5d1c9b52-3a79-4c0d-8e4f-1b2a3c4d5e6f", "eventRef": "{{Event}}", "pin": "482916", "qrText": "NODO|EVT:44444444000000000000000000000003|PIN:482916", "capacity": 35, "validFromUtc": "{{today.AddDays(20):yyyy-MM-dd}}T13:00:00Z", "validUntilUtc": "{{today.AddDays(20):yyyy-MM-dd}}T18:00:00Z", "status": "ACTIVE", "revokedAtUtc": null }"""),
            nameof(Notification) => JsonNode.Parse(
                $$"""{ "id": "9a7c1e20-5b3d-4f6a-8c2e-0d1f2a3b4c5d", "to": "Dirección", "channel": "panel", "subject": "Nueva solicitud de evento", "body": "Dirección: nueva solicitud de Fundación Crecer para el {{today.AddDays(10):dd/MM}} (14:00-18:00, 60 personas). Debes responder hoy.", "sentAt": "{{today:yyyy-MM-dd}}T12:00:00Z" }"""),
            nameof(LedgerEntry) => JsonNode.Parse(
                $$"""{ "entryId": "2f4e6a8c-1b3d-4f5a-9c7e-0a2b4c6d8e1f", "atUtc": "{{today:yyyy-MM-dd}}T16:00:00Z", "kind": "LiquidacionFinalCompletada", "account": "{{TechSol}}", "reference": "44444444-0000-0000-0000-000000000004", "amount": 1260, "currency": "BOB", "note": "Saldo restante liquidado: total 2520.00 BOB menos depósito 1260.00 BOB." }"""),
            nameof(CateringOrder) => JsonNode.Parse(
                $$"""{ "orderId": "7e1d3f5a-9c2b-4a6d-8e0f-1a3c5e7b9d2f", "eventRef": "{{Event}}", "receivedAtUtc": "{{today:yyyy-MM-dd}}T14:00:00Z", "status": "RECEIVED", "lines": [ { "sku": "COFFEE_BREAK", "title": "Coffee break", "headcount": 35, "pricePerHead": 35 } ] }"""),
            _ => null
        };
    }
}
