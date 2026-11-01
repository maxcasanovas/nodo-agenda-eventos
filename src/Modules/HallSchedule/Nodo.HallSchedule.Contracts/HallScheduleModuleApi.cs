namespace Nodo.HallSchedule.Contracts;

/// <summary>Own-equipment units requested for an event.</summary>
public sealed record EquipmentRequest(string Code, int Quantity);

/// <summary>
/// Outcome of a synchronous call to the schedule. When <see cref="Success"/> is false, <see cref="Code"/> is the
/// invariant code of the catalog (e.g. SCHEDULE.SLOT_OVERLAP) and <see cref="Reason"/> the Spanish explanation.
/// </summary>
public sealed record ScheduleResult(bool Success, string? Code, string? Reason)
{
    public static ScheduleResult Ok() => new(true, null, null);

    public static ScheduleResult Fail(string code, string reason) => new(false, code, reason);
}

/// <summary>Synchronous module API published by HallSchedule (Quotes and EventLifecycle are its customers).</summary>
public interface IHallScheduleModuleApi
{
    /// <summary>Read-only availability check. <paramref name="excludeEventId"/> ignores that event's own block.</summary>
    Task<ScheduleResult> CheckAvailabilityAsync(
        Guid hallId, DateOnly date, TimeOnly start, TimeOnly end,
        IReadOnlyCollection<EquipmentRequest> equipment, Guid? excludeEventId = null,
        CancellationToken cancellationToken = default);

    Task<ScheduleResult> PlaceProvisionalBlockAsync(
        Guid eventId, Guid hallId, DateOnly date, TimeOnly start, TimeOnly end,
        IReadOnlyCollection<EquipmentRequest> equipment, CancellationToken cancellationToken = default);

    Task<ScheduleResult> PlaceDefinitiveBlockAsync(
        Guid eventId, Guid hallId, DateOnly date, TimeOnly start, TimeOnly end,
        IReadOnlyCollection<EquipmentRequest> equipment, CancellationToken cancellationToken = default);
}
