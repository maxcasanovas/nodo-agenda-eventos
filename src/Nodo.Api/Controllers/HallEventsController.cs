using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nodo.Api.Contracts;
using Nodo.Api.OpenApi;
using Nodo.EventLifecycle.Application;

namespace Nodo.Api.Controllers;

[Route("api/hall-events")]
[Tags("Ciclo del Evento")]
public sealed class HallEventsController(IMediator mediator) : NodoControllerBase
{
    [HttpGet]
    [EndpointSummary("Lista eventos")]
    [EndpointDescription("Filtra por estado (`Requested`, `ApprovedByManagement`, `Confirmed`, `InProgress`, `Completed`, `Rejected`, `Cancelled`, `Expired`), cliente y rango de fechas.")]
    [ProducesResponseType<IReadOnlyCollection<HallEventListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<HallEventListItemDto>>> List(
        [FromQuery] string? status, [FromQuery] Guid? customerId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        CancellationToken ct) =>
        Ok(await mediator.Send(new ListHallEventsQuery(status, customerId, from, to), ct));

    [HttpGet("pending-approvals")]
    [EndpointSummary("Bandeja de Dirección")]
    [EndpointDescription("Solicitudes en estado `Requested`, las más urgentes primero (por plazo de decisión).")]
    [ProducesResponseType<IReadOnlyCollection<HallEventListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<HallEventListItemDto>>> PendingApprovals(CancellationToken ct) =>
        Ok(await mediator.Send(new GetPendingApprovalsQuery(), ct));

    [HttpGet("today")]
    [EndpointSummary("Operaciones de hoy")]
    [EndpointDescription("Eventos confirmados o en curso cuya fecha es el día de hoy en hora local del salón (America/La_Paz).")]
    [ProducesResponseType<IReadOnlyCollection<HallEventListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<HallEventListItemDto>>> Today(CancellationToken ct) =>
        Ok(await mediator.Send(new GetTodayOperationsQuery(), ct));

    [HttpGet("{id:guid}")]
    [EndpointSummary("Obtiene un evento")]
    [EndpointDescription("Incluye la línea de tiempo (`history`), el depósito, los servicios extra y el nombre del cliente.")]
    [ProducesResponseType<HallEventDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<HallEventDto>> Get(Guid id, CancellationToken ct) =>
        Ok(await mediator.Send(new GetHallEventQuery(id), ct));

    [HttpPost("{id:guid}/approve")]
    [EndpointSummary("Aprueba la solicitud (Dirección)")]
    [EndpointDescription(
        "Fija el porcentaje del depósito de garantía (20 a 50 %). El cliente tiene 48 horas para cubrirlo; la pasarela " +
        "crea el link de pago de forma asíncrona (`GET /api/external/payment-links`).")]
    [Invariants("INV-02", "INV-06", "INV-07")]
    [ProducesResponseType<HallEventStatusDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<HallEventStatusDto>> Approve(Guid id, ApproveRequest request, CancellationToken ct) =>
        Ok(await mediator.Send(new ApproveEventRequestCommand(id, request.DepositPercentage, request.ApprovedBy), ct));

    [HttpPost("{id:guid}/reject")]
    [EndpointSummary("Rechaza la solicitud (Dirección)")]
    [EndpointDescription("Requiere un motivo de al menos 10 caracteres. Libera el bloqueo provisional del salón.")]
    [Invariants("INV-06", "INV-07", "INV-08")]
    [ProducesResponseType<HallEventStatusDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<HallEventStatusDto>> Reject(Guid id, RejectRequest request, CancellationToken ct) =>
        Ok(await mediator.Send(new RejectEventRequestCommand(id, request.Reason, request.RejectedBy), ct));

    [HttpPost("{id:guid}/start")]
    [EndpointSummary("Inicia el evento (Operaciones)")]
    [EndpointDescription("Solo desde 1 hora antes del inicio y antes del fin. Para probarlo en la demo, adelanta el reloj con `POST /api/demo/clock/advance`.")]
    [Invariants("INV-06", "INV-11")]
    [ProducesResponseType<HallEventStatusDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<HallEventStatusDto>> Start(Guid id, PerformedByRequest? request, CancellationToken ct) =>
        Ok(await mediator.Send(new StartHallEventCommand(id, request?.PerformedBy ?? Staff.Operations), ct));

    [HttpPost("{id:guid}/complete")]
    [EndpointSummary("Cierra el evento (Operaciones)")]
    [EndpointDescription("Facturación liquida el saldo restante: total menos el depósito acreditado (`GET /api/external/billing/ledger`).")]
    [Invariants("INV-06")]
    [ProducesResponseType<HallEventStatusDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<HallEventStatusDto>> Complete(Guid id, PerformedByRequest? request, CancellationToken ct) =>
        Ok(await mediator.Send(new CompleteHallEventCommand(id, request?.PerformedBy ?? Staff.Operations), ct));

    [HttpPost("{id:guid}/cancel")]
    [EndpointSummary("Cancela el evento")]
    [EndpointDescription(
        "Permitido en `Requested`, `ApprovedByManagement` y `Confirmed`, siempre antes del inicio. **Nunca hay devolución de dinero**: " +
        "todo lo acreditado pasa íntegro a saldo a favor del cliente (`GET /api/customers/{id}/credit`).")]
    [Invariants("INV-05")]
    [ProducesResponseType<HallEventStatusDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<HallEventStatusDto>> Cancel(Guid id, CancelRequest request, CancellationToken ct) =>
        Ok(await mediator.Send(new CancelHallEventCommand(id, request.Reason, request.CancelledBy), ct));

    [HttpPost("{id:guid}/reschedule")]
    [EndpointSummary("Reprograma un evento cancelado con saldo a favor")]
    [EndpointDescription(
        "Conserva duración, aforo, servicios y total; solo cambian el día y la hora de inicio. Se aplica el saldo a favor hasta cubrir " +
        "el depósito: si lo cubre queda `Confirmed` con bloqueo definitivo; si no, `ApprovedByManagement` con 48 horas para pagar la diferencia.")]
    [Invariants("INV-12", "INV-16", "INV-18", "INV-20", "INV-21")]
    [ProducesResponseType<RescheduleResultDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RescheduleResultDto>> Reschedule(Guid id, RescheduleRequest request, CancellationToken ct) =>
        Ok(await mediator.Send(new RescheduleHallEventCommand(id, request.NewDate, request.NewStartTime, request.PerformedBy), ct));
}

[Route("api/customers")]
[Tags("Ciclo del Evento")]
public sealed class CustomersController(IMediator mediator) : NodoControllerBase
{
    [HttpGet("{id:guid}/credit")]
    [EndpointSummary("Saldo a favor de un cliente")]
    [EndpointDescription("Balance y movimientos. Un cliente sin saldo devuelve balance 0. El saldo solo se usa para reprogramar; nunca se devuelve en dinero (INV-22).")]
    [ProducesResponseType<CustomerCreditDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CustomerCreditDto>> Credit(Guid id, CancellationToken ct) =>
        Ok(await mediator.Send(new GetCustomerCreditQuery(id), ct));
}
