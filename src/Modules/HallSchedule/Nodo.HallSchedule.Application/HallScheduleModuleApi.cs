using Nodo.BuildingBlocks.Application;
using Nodo.HallSchedule.Contracts;
using Nodo.HallSchedule.Domain;
using Nodo.SharedKernel;

namespace Nodo.HallSchedule.Application;

/// <summary>
/// Synchronous module API. Orchestration only: it builds domain types, asks the aggregate, saves, and translates
/// a violated invariant into a <see cref="ScheduleResult"/> that carries the catalog code.
/// </summary>
public sealed class HallScheduleModuleApi(
    IHallScheduleRepository schedules,
    IHallScheduleUnitOfWork unitOfWork,
    ISpacesCatalog spaces,
    IClock clock) : IHallScheduleModuleApi
{
    public async Task<ScheduleResult> CheckAvailabilityAsync(
        Guid hallId, DateOnly date, TimeOnly start, TimeOnly end,
        IReadOnlyCollection<EquipmentRequest> equipment, Guid? excludeEventId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var slot = TimeSlot.Create(date, start, end);
            var allocations = ToAllocations(equipment);
            var schedule = await schedules.GetOrCreateAsync(new HallId(hallId), date, cancellationToken);
            var inventory = await spaces.GetEquipmentStockAsync(cancellationToken);

            var result = AvailabilityChecker.Check(
                schedule, slot, allocations, inventory, excludeEventId is null ? null : new EventId(excludeEventId.Value));

            return result.IsAvailable ? ScheduleResult.Ok() : ScheduleResult.Fail(result.Code!, result.Reason!);
        }
        catch (DomainException ex)
        {
            return ScheduleResult.Fail(ex.Code, ex.Message);
        }
    }

    public Task<ScheduleResult> PlaceProvisionalBlockAsync(
        Guid eventId, Guid hallId, DateOnly date, TimeOnly start, TimeOnly end,
        IReadOnlyCollection<EquipmentRequest> equipment, CancellationToken cancellationToken = default) =>
        PlaceAsync(eventId, hallId, date, start, end, equipment, definitive: false, cancellationToken);

    public Task<ScheduleResult> PlaceDefinitiveBlockAsync(
        Guid eventId, Guid hallId, DateOnly date, TimeOnly start, TimeOnly end,
        IReadOnlyCollection<EquipmentRequest> equipment, CancellationToken cancellationToken = default) =>
        PlaceAsync(eventId, hallId, date, start, end, equipment, definitive: true, cancellationToken);

    private async Task<ScheduleResult> PlaceAsync(
        Guid eventId, Guid hallId, DateOnly date, TimeOnly start, TimeOnly end,
        IReadOnlyCollection<EquipmentRequest> equipment, bool definitive, CancellationToken cancellationToken)
    {
        HallScheduleAggregate schedule;
        try
        {
            var slot = TimeSlot.Create(date, start, end);
            var allocations = ToAllocations(equipment);
            schedule = await schedules.GetOrCreateAsync(new HallId(hallId), date, cancellationToken);
            var inventory = await spaces.GetEquipmentStockAsync(cancellationToken);

            if (definitive)
                schedule.PlaceDefinitiveBlock(new EventId(eventId), slot, allocations, inventory, clock.UtcNow);
            else
                schedule.PlaceProvisionalBlock(new EventId(eventId), slot, allocations, inventory, clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return ScheduleResult.Fail(ex.Code, ex.Message);
        }

        await schedules.SaveAsync(schedule, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ScheduleResult.Ok();
    }

    private static List<EquipmentAllocation> ToAllocations(IReadOnlyCollection<EquipmentRequest> equipment) =>
        equipment.Select(e => EquipmentAllocation.Create(e.Code, e.Quantity)).ToList();
}
