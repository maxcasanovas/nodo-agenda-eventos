using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nodo.HallSchedule.Application;
using Nodo.HallSchedule.Contracts;

namespace Nodo.Api.Controllers;

[Route("api/hall-schedule")]
[Tags("Agenda del Salón")]
public sealed class HallScheduleController(IMediator mediator) : NodoControllerBase
{
    [HttpGet("{hallId:guid}")]
    [EndpointSummary("Calendario del salón")]
    [EndpointDescription(
        "Bloques activos entre `from` y `to` con su franja, los buffers de 1 h de montaje y limpieza, el tipo (`Provisional` o `Definitive`), " +
        "el equipo reservado y el evento y cliente asociados. Pensado para FullCalendar.")]
    [ProducesResponseType<IReadOnlyCollection<CalendarBlockDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<CalendarBlockDto>>> Calendar(
        Guid hallId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        Ok(await mediator.Send(new GetHallCalendarQuery(hallId, from, to), ct));

    [HttpGet("{hallId:guid}/availability")]
    [EndpointSummary("Consulta de disponibilidad")]
    [EndpointDescription(
        "Informa si la franja y los equipos están libres, **sin reservar nada**. `equipment` es una lista `CODIGO:CANTIDAD` separada por comas " +
        "(por ejemplo `PROJECTOR:1,TV:2`). Si no está disponible, `code` es `SCHEDULE.SLOT_OVERLAP` o `SCHEDULE.EQUIPMENT_UNAVAILABLE`.")]
    [ProducesResponseType<AvailabilityDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AvailabilityDto>> Availability(
        Guid hallId, [FromQuery] DateOnly date, [FromQuery] TimeOnly start, [FromQuery] TimeOnly end,
        [FromQuery] string? equipment, CancellationToken ct) =>
        Ok(await mediator.Send(new CheckAvailabilityQuery(hallId, date, start, end, ParseEquipment(equipment)), ct));

    private static List<EquipmentRequest> ParseEquipment(string? text)
    {
        var result = new List<EquipmentRequest>();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split(':', StringSplitOptions.TrimEntries);
            if (pieces.Length != 2 || pieces[0].Length == 0 || !int.TryParse(pieces[1], out var quantity))
            {
                throw new ValidationException([new ValidationFailure("equipment", $"'{part}' no tiene el formato CODIGO:CANTIDAD.")]);
            }

            result.Add(new EquipmentRequest(pieces[0], quantity));
        }

        return result;
    }
}
