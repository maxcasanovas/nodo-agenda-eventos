using Nodo.SharedKernel;

namespace Nodo.Quotes.Domain;

/// <summary>Domain service (BR-06..08): turns duration, membership discount and extras into a price breakdown.</summary>
public static class EventPricingCalculator
{
    public static PriceBreakdown Calculate(
        int hours,
        int membershipDiscountPercentage,
        IEnumerable<ExtraServiceLine> extras,
        DegressiveRate? rate = null)
    {
        var extrasTotal = extras.Aggregate(Money.Zero, (sum, line) => sum.Add(line.LineTotal));
        return PriceBreakdown.Create(rate ?? DegressiveRate.Standard, hours, membershipDiscountPercentage, extrasTotal);
    }
}
