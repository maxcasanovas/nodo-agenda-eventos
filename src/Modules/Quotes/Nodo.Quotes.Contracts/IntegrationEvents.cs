using Nodo.BuildingBlocks.Application;

namespace Nodo.Quotes.Contracts;

/// <summary>An extra service frozen in the quote. <see cref="Type"/> is "OwnEquipment" or "ExternalCatering".</summary>
public sealed record QuoteExtraServiceDto(string Type, string Code, string Name, int Quantity, decimal UnitCost);

public sealed record QuoteIssuedIntegrationEvent(
    Guid Id, DateTime OccurredOnUtc, Guid QuoteId, Guid CustomerId, DateOnly Date, TimeOnly Start, TimeOnly End,
    decimal Total, DateTime ExpiresAtUtc) : IIntegrationEvent;

/// <summary>Full snapshot: EventLifecycle creates the event request from it without calling back (BR-11).</summary>
public sealed record QuoteAcceptedIntegrationEvent(
    Guid Id, DateTime OccurredOnUtc, Guid QuoteId, Guid CustomerId, Guid HallId, DateOnly Date, TimeOnly Start,
    TimeOnly End, int Attendance, decimal BaseHourlyRate, int Hours, decimal RateFactor, decimal HallRental,
    int MembershipDiscountPercentage, decimal MembershipDiscount, decimal ExtrasTotal, decimal Total,
    IReadOnlyCollection<QuoteExtraServiceDto> ExtraServices) : IIntegrationEvent;

public sealed record QuoteExpiredIntegrationEvent(
    Guid Id, DateTime OccurredOnUtc, Guid QuoteId, Guid CustomerId) : IIntegrationEvent;
