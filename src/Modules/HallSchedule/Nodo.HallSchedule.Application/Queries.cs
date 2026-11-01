using FluentValidation;
using Nodo.BuildingBlocks.Application;
using Nodo.EventLifecycle.Contracts;
using Nodo.HallSchedule.Contracts;
using Nodo.HallSchedule.Domain;

namespace Nodo.HallSchedule.Application;

public sealed record CalendarBlockDto(
    Guid BlockId, Guid EventId, DateOnly Date, TimeOnly Start, TimeOnly End, TimeOnly BufferStart, TimeOnly BufferEnd,
    string Kind, IReadOnlyCollection<EquipmentRequest> Equipment, Guid? CustomerId, string? CustomerName, string? EventStatus);

public sealed record AvailabilityDto(bool IsAvailable, string? Code, string? Reason);

// ---- GetHallCalendar ----

public sealed record GetHallCalendarQuery(Guid HallId, DateOnly From, DateOnly To)
    : IQuery<IReadOnlyCollection<CalendarBlockDto>>;

internal sealed class GetHallCalendarValidator : AbstractValidator<GetHallCalendarQuery>
{
    public GetHallCalendarValidator()
    {
        RuleFor(x => x.HallId).NotEmpty().WithMessage("El salón es obligatorio.");
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From).WithMessage("El rango de fechas es inválido.");
    }
}

internal sealed class GetHallCalendarHandler(IHallCalendarQueries queries, IEventLifecycleModuleApi eventLifecycle)
    : IQueryHandler<GetHallCalendarQuery, IReadOnlyCollection<CalendarBlockDto>>
{
    public async Task<IReadOnlyCollection<CalendarBlockDto>> Handle(
        GetHallCalendarQuery request, CancellationToken cancellationToken)
    {
        var blocks = await queries.GetActiveBlocksAsync(request.HallId, request.From, request.To, cancellationToken);
        if (blocks.Count == 0)
            return [];

        var summaries = (await eventLifecycle.GetEventSummariesAsync(
                blocks.Select(b => b.EventId).Distinct().ToList(), cancellationToken))
            .ToDictionary(s => s.EventId);

        return blocks
            .OrderBy(b => b.Date).ThenBy(b => b.Start)
            .Select(b =>
            {
                var slot = TimeSlot.Create(b.Date, b.Start, b.End);
                summaries.TryGetValue(b.EventId, out var summary);
                return new CalendarBlockDto(
                    b.BlockId, b.EventId, b.Date, b.Start, b.End,
                    ToTime(slot.BufferedStartMinute), ToTime(slot.BufferedEndMinute),
                    b.Kind, b.Equipment, summary?.CustomerId, summary?.CustomerName, summary?.Status);
            })
            .ToList();
    }

    private static TimeOnly ToTime(int minuteOfDay) =>
        TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(Math.Clamp(minuteOfDay, 0, 24 * 60 - 1)));
}

// ---- CheckAvailability (for the UI) ----

public sealed record CheckAvailabilityQuery(
    Guid HallId, DateOnly Date, TimeOnly Start, TimeOnly End,
    IReadOnlyCollection<EquipmentRequest> Equipment, Guid? ExcludeEventId = null) : IQuery<AvailabilityDto>;

internal sealed class CheckAvailabilityValidator : AbstractValidator<CheckAvailabilityQuery>
{
    public CheckAvailabilityValidator()
    {
        RuleFor(x => x.HallId).NotEmpty().WithMessage("El salón es obligatorio.");
        RuleFor(x => x.Equipment).NotNull().WithMessage("La lista de equipos es obligatoria (puede estar vacía).");
    }
}

internal sealed class CheckAvailabilityHandler(IHallScheduleModuleApi api)
    : IQueryHandler<CheckAvailabilityQuery, AvailabilityDto>
{
    public async Task<AvailabilityDto> Handle(CheckAvailabilityQuery request, CancellationToken cancellationToken)
    {
        var result = await api.CheckAvailabilityAsync(
            request.HallId, request.Date, request.Start, request.End, request.Equipment, request.ExcludeEventId,
            cancellationToken);
        return new AvailabilityDto(result.Success, result.Code, result.Reason);
    }
}
