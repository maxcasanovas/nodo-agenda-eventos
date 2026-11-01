using Nodo.SharedKernel;

namespace Nodo.EventLifecycle.Domain;

public sealed record EventRequestCreated(
    HallEventId EventId, HallId HallId, DateOnly Date, TimeOnly Start, TimeOnly End,
    int Attendance, CustomerId CustomerId, DateTime OccurredOnUtc) : IDomainEvent;

public sealed record HallEventApprovedByManagement(
    HallEventId EventId, string ApprovedBy, int DepositPercentage, Money DepositRequired,
    DateTime DepositDeadlineUtc, DateTime OccurredOnUtc) : IDomainEvent;

public sealed record EventRequestRejected(
    HallEventId EventId, HallId HallId, DateOnly Date, string RejectedBy, string Reason, DateTime OccurredOnUtc) : IDomainEvent;

public sealed record GuaranteeDepositAccredited(
    HallEventId EventId, Money Amount, string TransactionId, Money AccreditedTotal, bool IsCovered,
    DateTime OccurredOnUtc) : IDomainEvent;

public sealed record HallEventFirmlyConfirmed(
    HallEventId EventId, HallId HallId, DateOnly Date, TimeOnly Start, TimeOnly End, int Attendance,
    CustomerId CustomerId, Money Total, Money DepositAccredited,
    IReadOnlyCollection<ExtraServiceItem> ExtraServices, DateTime OccurredOnUtc) : IDomainEvent;

public sealed record ExtraServicesAssigned(
    HallEventId EventId, IReadOnlyCollection<ExtraServiceItem> ExtraServices, DateTime OccurredOnUtc) : IDomainEvent;

public sealed record HallEventStarted(HallEventId EventId, string StartedBy, DateTime OccurredOnUtc) : IDomainEvent;

public sealed record HallEventCompleted(
    HallEventId EventId, CustomerId CustomerId, Money Total, Money DepositAccredited, Money RemainingToSettle,
    DateTime OccurredOnUtc) : IDomainEvent;

public sealed record HallEventCancelledWithCredit(
    HallEventId EventId, HallId HallId, DateOnly Date, CustomerId CustomerId, Money CreditAmount,
    DateTime OccurredOnUtc) : IDomainEvent;

public sealed record HallEventRescheduledWithCredit(
    HallEventId EventId, HallId HallId, CustomerId CustomerId, DateOnly NewDate, TimeOnly NewStart, TimeOnly NewEnd,
    Money CreditApplied, HallEventStatus ResultingStatus, Money DepositPending, DateTime? DepositDeadlineUtc,
    DateTime OccurredOnUtc) : IDomainEvent;

public sealed record HallEventExpired(
    HallEventId EventId, HallId HallId, DateOnly Date, CustomerId CustomerId, Money CreditAmount,
    DateTime OccurredOnUtc) : IDomainEvent;

public sealed record CustomerCreditGranted(
    CustomerId CustomerId, HallEventId EventId, Money Amount, Money Balance, DateTime OccurredOnUtc) : IDomainEvent;

public sealed record CustomerCreditApplied(
    CustomerId CustomerId, HallEventId EventId, Money Amount, Money Balance, DateTime OccurredOnUtc) : IDomainEvent;
