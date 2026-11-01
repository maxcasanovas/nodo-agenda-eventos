using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nodo.Api.Contracts;
using Nodo.Api.OpenApi;
using Nodo.External.Simulated.Spaces;
using Nodo.Quotes.Application;

namespace Nodo.Api.Controllers;

/// <summary>Base of the business controllers: every endpoint can answer 400, 404, 409 and 422 as ProblemDetails.</summary>
[ApiController]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
public abstract class NodoControllerBase : ControllerBase;

[Route("api/quotes")]
[Tags("Cotizaciones")]
public sealed class QuotesController(IMediator mediator, SpacesContext spaces) : NodoControllerBase
{
    private Guid DefaultHall => Guid.Parse(spaces.Venues[0].VenueUid);

    [HttpPost("preview")]
    [EndpointSummary("Previsualiza una cotización")]
    [EndpointDescription(
        "Calcula el precio (tarifa degresiva, descuento de membresía y servicios extra) y la disponibilidad del salón " +
        "**sin guardar nada**. La disponibilidad es solo informativa: emitir una cotización no bloquea el calendario (BR-10).")]
    [Invariants("INV-01", "INV-04", "INV-13", "INV-16", "INV-17")]
    [ProducesResponseType<QuotePreviewDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<QuotePreviewDto>> Preview(PreviewQuoteRequest request, CancellationToken ct) =>
        Ok(await mediator.Send(new PreviewQuoteQuery(
            request.CustomerId, request.HallId ?? DefaultHall, request.Date, request.StartTime, request.EndTime,
            request.Attendance, request.ExtraServices ?? []), ct));

    [HttpPost]
    [EndpointSummary("Emite una cotización")]
    [EndpointDescription(
        "Crea y guarda una cotización con vigencia de 96 horas. El precio y los servicios quedan congelados. " +
        "Devuelve el desglose y la disponibilidad informativa del salón.")]
    [Invariants("INV-01", "INV-04", "INV-13", "INV-16", "INV-17")]
    [ProducesResponseType<IssuedQuoteDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IssuedQuoteDto>> Issue(IssueQuoteRequest request, CancellationToken ct) =>
        Ok(await mediator.Send(new IssueQuoteCommand(
            request.PerformedBy ?? Staff.Reception, request.CustomerId, request.HallId ?? DefaultHall, request.Date,
            request.StartTime, request.EndTime, request.Attendance, request.ExtraServices ?? []), ct));

    [HttpGet]
    [EndpointSummary("Lista cotizaciones")]
    [EndpointDescription("Filtra por cliente y/o estado (`Issued`, `Accepted`, `Expired`). Incluye `expiresAtUtc` y las horas restantes de vigencia.")]
    [ProducesResponseType<IReadOnlyCollection<QuoteDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<QuoteDto>>> List(
        [FromQuery] Guid? customerId, [FromQuery] string? status, CancellationToken ct) =>
        Ok(await mediator.Send(new ListQuotesQuery(customerId, status), ct));

    [HttpGet("{id:guid}")]
    [EndpointSummary("Obtiene una cotización")]
    [EndpointDescription("Precio congelado, servicios, estado y las horas que le quedan de vigencia (0 si ya no está en ` Issued `).")]
    [ProducesResponseType<QuoteDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<QuoteDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await mediator.Send(new GetQuoteQuery(id), ct));

    [HttpPost("{id:guid}/accept")]
    [EndpointSummary("Acepta una cotización")]
    [EndpointDescription(
        "Exige que el salón siga disponible (BR-10) y congela el precio. La solicitud de evento y el bloqueo provisional " +
        "se crean **de forma asíncrona** a través del outbox: consulta `GET /api/hall-events` unos segundos después.")]
    [Invariants("INV-14", "INV-16", "INV-18", "INV-20")]
    [ProducesResponseType<AcceptedQuoteDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AcceptedQuoteDto>> Accept(Guid id, AcceptQuoteRequest? request, CancellationToken ct) =>
        Ok(await mediator.Send(new AcceptQuoteCommand(id, request?.AcceptedBy ?? Staff.Customer), ct));
}
