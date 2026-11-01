using Nodo.Quotes.Domain;

namespace Nodo.Quotes.Application;

public sealed record PriceBreakdownDto(
    decimal BaseHourlyRate, int Hours, decimal RateFactor, decimal HallRental, int MembershipDiscountPercentage,
    decimal MembershipDiscount, decimal ExtrasTotal, decimal Total);

public sealed record ExtraServiceLineDto(
    string Type, string Code, string Name, int Quantity, decimal UnitCost, decimal LineTotal);

/// <summary>Informative availability (BR-10): emitting a quote never blocks the calendar.</summary>
public sealed record AvailabilityInfoDto(bool IsAvailable, string? Code, string? Reason);

public sealed record QuotePreviewDto(
    PriceBreakdownDto Price, IReadOnlyCollection<ExtraServiceLineDto> ExtraServices, AvailabilityInfoDto Availability);

public sealed record IssuedQuoteDto(
    Guid QuoteId, DateTime IssuedAtUtc, DateTime ExpiresAtUtc, PriceBreakdownDto Price,
    IReadOnlyCollection<ExtraServiceLineDto> ExtraServices, AvailabilityInfoDto Availability);

public sealed record AcceptedQuoteDto(Guid QuoteId, string Status, DateTime AcceptedAtUtc);

public sealed record QuoteDto(
    Guid Id, Guid CustomerId, string? CustomerName, Guid HallId, DateOnly Date, TimeOnly Start, TimeOnly End,
    int Attendance, string Status, DateTime IssuedAtUtc, DateTime ExpiresAtUtc, DateTime? AcceptedAtUtc,
    double HoursRemaining, PriceBreakdownDto Price, IReadOnlyCollection<ExtraServiceLineDto> ExtraServices);

/// <summary>A requested extra. A null quantity means 1 for equipment and the attendance for catering (BR-08).</summary>
public sealed record ExtraServiceRequest(string Code, int? Quantity = null);

internal static class QuoteMapping
{
    public static PriceBreakdownDto ToDto(this PriceBreakdown p) => new(
        p.BaseHourlyRate.Amount, p.Hours, p.RateFactor, p.HallRental.Amount, p.MembershipDiscountPercentage,
        p.MembershipDiscount.Amount, p.ExtrasTotal.Amount, p.Total.Amount);

    public static ExtraServiceLineDto ToDto(this ExtraServiceLine l) => new(
        l.Type.ToString(), l.Code, l.Name, l.Quantity, l.UnitCost.Amount, l.LineTotal.Amount);
}
