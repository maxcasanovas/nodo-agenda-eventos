using Nodo.BuildingBlocks.Application;

namespace Nodo.EventLifecycle.Contracts;

// Amounts are decimals in BOB. Payloads use primitives only (business-rules.md §7).

/// <summary>Extra service of an event. <see cref="Type"/> is "OwnEquipment" or "ExternalCatering".</summary>
public sealed record EventExtraServiceDto(string Type, string Code, string Description, int Quantity, decimal UnitCost);

public sealed record EventRequestCreatedIntegrationEvent(
    Guid Id, DateTime OccurredOnUtc, Guid EventId, Guid HallId, DateOnly Date, TimeOnly Start, TimeOnly End,
    int Attendance, Guid CustomerId) : IIntegrationEvent;

public sealed record HallEventApprovedByManagementIntegrationEvent(
    Guid Id, DateTime OccurredOnUtc, Guid EventId, string ApprovedBy, int DepositPercentage, decimal DepositRequired,
    DateTime DepositDeadlineUtc) : IIntegrationEvent;

/// <summary>HallId and Date let HallSchedule find the day's schedule to release.</summary>
public sealed record EventRequestRejectedIntegrationEvent(
    Guid Id, DateTime OccurredOnUtc, Guid EventId, Guid HallId, DateOnly Date, string RejectedBy, string Reason)
    : IIntegrationEvent;

public sealed record GuaranteeDepositAccreditedIntegrationEvent(
    Guid Id, DateTime OccurredOnUtc, Guid EventId, decimal Amount, string TransactionId, decimal AccreditedTotal,
    bool IsCovered) : IIntegrationEvent;

public sealed record HallEventFirmlyConfirmedIntegrationEvent(
    Guid Id, DateTime OccurredOnUtc, Guid EventId, Guid HallId, DateOnly Date, TimeOnly Start, TimeOnly End,
    int Attendance, Guid CustomerId, decimal Total, decimal DepositAccredited,
    IReadOnlyCollection<EventExtraServiceDto> ExtraServices) : IIntegrationEvent;

public sealed record ExtraServicesAssignedIntegrationEvent(
    Guid Id, DateTime OccurredOnUtc, Guid EventId, IReadOnlyCollection<EventExtraServiceDto> ExtraServices)
    : IIntegrationEvent;

public sealed record HallEventStartedIntegrationEvent(
    Guid Id, DateTime OccurredOnUtc, Guid EventId, string StartedBy) : IIntegrationEvent;

public sealed record HallEventCompletedIntegrationEvent(
    Guid Id, DateTime OccurredOnUtc, Guid EventId, Guid CustomerId, decimal Total, decimal DepositAccredited,
    decimal RemainingToSettle) : IIntegrationEvent;

public sealed record HallEventCancelledWithCreditIntegrationEvent(
    Guid Id, DateTime OccurredOnUtc, Guid EventId, Guid HallId, DateOnly Date, Guid CustomerId, decimal CreditAmount)
    : IIntegrationEvent;

/// <summary>
/// <see cref="ResultingStatus"/> is "Confirmed" or "ApprovedByManagement". When it is not confirmed yet,
/// <see cref="DepositPending"/> is what is still owed and <see cref="DepositDeadlineUtc"/> the new payment deadline.
/// </summary>
public sealed record HallEventRescheduledWithCreditIntegrationEvent(
    Guid Id, DateTime OccurredOnUtc, Guid EventId, Guid HallId, Guid CustomerId, DateOnly NewDate, TimeOnly NewStart,
    TimeOnly NewEnd, decimal CreditApplied, string ResultingStatus, decimal DepositPending, DateTime? DepositDeadlineUtc)
    : IIntegrationEvent;

public sealed record HallEventExpiredIntegrationEvent(
    Guid Id, DateTime OccurredOnUtc, Guid EventId, Guid HallId, DateOnly Date, Guid CustomerId, decimal CreditAmount)
    : IIntegrationEvent;

public sealed record CustomerCreditGrantedIntegrationEvent(
    Guid Id, DateTime OccurredOnUtc, Guid CustomerId, Guid EventId, decimal Amount, decimal Balance)
    : IIntegrationEvent;

public sealed record CustomerCreditAppliedIntegrationEvent(
    Guid Id, DateTime OccurredOnUtc, Guid CustomerId, Guid EventId, decimal Amount, decimal Balance)
    : IIntegrationEvent;
