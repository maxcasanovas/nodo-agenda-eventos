using Nodo.SharedKernel;

namespace Nodo.Quotes.Domain;

public sealed record QuoteIssued(
    QuoteId QuoteId, CustomerId CustomerId, DateOnly Date, TimeOnly Start, TimeOnly End,
    Money Total, DateTime ExpiresAtUtc, DateTime OccurredOnUtc) : IDomainEvent;

/// <summary>Full snapshot: EventLifecycle creates the event request from it without calling back.</summary>
public sealed record QuoteAccepted(
    QuoteId QuoteId, CustomerId CustomerId, HallId HallId, TimeSlot Slot, Attendance Attendance,
    IReadOnlyCollection<ExtraServiceLine> ExtraServices, PriceBreakdown Price, DateTime OccurredOnUtc) : IDomainEvent;

public sealed record QuoteExpired(QuoteId QuoteId, CustomerId CustomerId, DateTime OccurredOnUtc) : IDomainEvent;
