using Nodo.EventLifecycle.Contracts;
using Nodo.EventLifecycle.Domain;
using Nodo.HallSchedule.Contracts;

namespace Nodo.EventLifecycle.Application;

public sealed record HistoryEntryDto(string Status, DateTime OccurredAtUtc, string Actor, string Note);

public sealed record DepositDto(
    int Percentage, decimal Required, decimal Accredited, decimal Pending, bool IsCovered,
    IReadOnlyCollection<string> TransactionIds);

public sealed record ExtraServiceDto(
    string Type, string Code, string Description, int Quantity, decimal UnitCost, decimal LineTotal);

public sealed record ManagementDecisionDto(string DecidedBy, DateTime DecidedAtUtc, bool Approved, string? RejectionReason);

public sealed record HallEventDto(
    Guid Id, Guid QuoteId, Guid CustomerId, string? CustomerName, Guid HallId, DateOnly Date, TimeOnly Start,
    TimeOnly End, int Attendance, decimal Total, string Status, DepositDto? Deposit, DateTime RequestedAtUtc,
    DateTime DecisionDeadlineUtc, DateTime? DepositDeadlineUtc, ManagementDecisionDto? Decision, decimal CreditGenerated,
    int RescheduleCount, bool ExtraServicesAssigned, decimal? RemainingToSettle,
    IReadOnlyCollection<ExtraServiceDto> ExtraServices, IReadOnlyCollection<HistoryEntryDto> History);

public sealed record HallEventListItemDto(
    Guid Id, Guid CustomerId, string? CustomerName, DateOnly Date, TimeOnly Start, TimeOnly End, int Attendance,
    decimal Total, string Status, DateTime DecisionDeadlineUtc, DateTime? DepositDeadlineUtc,
    decimal DepositRequired, decimal DepositAccredited);

public sealed record CreditMovementDto(string Type, decimal Amount, Guid EventId, string Reason, DateTime OccurredAtUtc);

public sealed record CustomerCreditDto(
    Guid CustomerId, string? CustomerName, decimal Balance, IReadOnlyCollection<CreditMovementDto> Movements);

// ---- Command results ----

public sealed record HallEventStatusDto(Guid EventId, string Status);

public sealed record AccreditDepositResultDto(
    Guid EventId, string Status, decimal AccreditedTotal, decimal Pending, bool IsCovered, decimal ExcessGrantedAsCredit);

public sealed record RescheduleResultDto(
    Guid EventId, string Status, decimal CreditApplied, decimal DepositPending, DateTime? DepositDeadlineUtc);

internal static class HallEventMapping
{
    /// <summary>The own-equipment units an event needs reserved in the hall's schedule (catering is not a hall resource).</summary>
    public static IReadOnlyCollection<EquipmentRequest> OwnEquipmentOf(HallEvent hallEvent) => hallEvent.ExtraServices
        .Where(e => e.Type == ExtraServiceType.OwnEquipment)
        .Select(e => new EquipmentRequest(e.Code, e.Quantity))
        .ToList();

    public static EventExtraServiceDto ToContract(this ExtraServiceItem item) =>
        new(item.Type.ToString(), item.Code, item.Description, item.Quantity, item.UnitCost.Amount);
}
