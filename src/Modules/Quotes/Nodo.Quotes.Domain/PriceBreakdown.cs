using Nodo.SharedKernel;

namespace Nodo.Quotes.Domain;

/// <summary>
/// Frozen price of a quote. Total = hall rental - membership discount + extras (BR-06..08).
/// The discount applies only to the hall rental (INV-17, BR-07).
/// </summary>
public sealed class PriceBreakdown : ValueObject
{
    public const int MaxMembershipDiscountPercentage = 10;

    // For EF Core only: rebuilds the value from its stored columns. Domain code always goes through Create.
    private PriceBreakdown()
    {
        BaseHourlyRate = null!;
        HallRental = null!;
        MembershipDiscount = null!;
        ExtrasTotal = null!;
        Total = null!;
    }

    private PriceBreakdown(
        Money baseHourlyRate, int hours, decimal rateFactor, Money hallRental,
        int membershipDiscountPercentage, Money membershipDiscount, Money extrasTotal, Money total)
    {
        BaseHourlyRate = baseHourlyRate;
        Hours = hours;
        RateFactor = rateFactor;
        HallRental = hallRental;
        MembershipDiscountPercentage = membershipDiscountPercentage;
        MembershipDiscount = membershipDiscount;
        ExtrasTotal = extrasTotal;
        Total = total;
    }

    public Money BaseHourlyRate { get; }

    public int Hours { get; }

    public decimal RateFactor { get; }

    public Money HallRental { get; }

    public int MembershipDiscountPercentage { get; }

    public Money MembershipDiscount { get; }

    public Money ExtrasTotal { get; }

    public Money Total { get; }

    internal static PriceBreakdown Create(DegressiveRate rate, int hours, int membershipDiscountPercentage, Money extrasTotal)
    {
        if (membershipDiscountPercentage is < 0 or > MaxMembershipDiscountPercentage)
        {
            throw new DomainException(InvariantCodes.InvalidDiscount,
                $"El descuento de membresía debe estar entre 0 % y {MaxMembershipDiscountPercentage} % " +
                $"(se indicó {membershipDiscountPercentage} %).");
        }

        var factor = rate.FactorFor(hours);
        var hallRental = Money.Of(rate.BaseHourlyRate.Amount * hours * factor, rate.BaseHourlyRate.Currency);
        var discount = hallRental.Percentage(membershipDiscountPercentage);
        var total = hallRental.Subtract(discount).Add(extrasTotal);

        return new PriceBreakdown(
            rate.BaseHourlyRate, hours, factor, hallRental, membershipDiscountPercentage, discount, extrasTotal, total);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return BaseHourlyRate;
        yield return Hours;
        yield return RateFactor;
        yield return HallRental;
        yield return MembershipDiscountPercentage;
        yield return MembershipDiscount;
        yield return ExtrasTotal;
        yield return Total;
    }
}
