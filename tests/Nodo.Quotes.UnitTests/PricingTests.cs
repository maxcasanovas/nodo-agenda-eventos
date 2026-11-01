using Nodo.Quotes.Domain;
using Nodo.SharedKernel;
using static Nodo.Quotes.UnitTests.TestData;

namespace Nodo.Quotes.UnitTests;

public class PricingTests
{
    // ---- BR-06: degressive rate, base 500 Bs/h, factor applied to all hours ----

    [Theory]
    [InlineData(1, 1.00, 500)]
    [InlineData(2, 1.00, 1000)]
    [InlineData(3, 0.90, 1350)]
    [InlineData(4, 0.90, 1800)]
    [InlineData(5, 0.80, 2000)]
    [InlineData(6, 0.80, 2400)]
    [InlineData(7, 0.70, 2450)]
    [InlineData(8, 0.70, 2800)]
    public void Calculate_Br06_AppliesTierFactorToAllHours(int hours, double factor, double expectedRental)
    {
        var price = EventPricingCalculator.Calculate(hours, 0, []);

        price.RateFactor.ShouldBe((decimal)factor);
        price.HallRental.Amount.ShouldBe((decimal)expectedRental);
        price.Total.Amount.ShouldBe((decimal)expectedRental);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void FactorFor_Br06_HoursOutsideOneToEight_ThrowsDurationOutOfRange(int hours) =>
        Should.Throw<DomainException>(() => DegressiveRate.Standard.FactorFor(hours))
            .Code.ShouldBe("EVENT.DURATION_OUT_OF_RANGE");

    [Fact]
    public void Calculate_Br06_ExposesBreakdownFields()
    {
        var price = EventPricingCalculator.Calculate(4, 0, []);

        price.BaseHourlyRate.Amount.ShouldBe(500m);
        price.Hours.ShouldBe(4);
        price.MembershipDiscount.Amount.ShouldBe(0m);
        price.ExtrasTotal.Amount.ShouldBe(0m);
    }

    // ---- BR-07: membership discount only on the hall rental ----

    [Theory]
    [InlineData(5, 90, 1710)]
    [InlineData(10, 180, 1620)]
    public void Calculate_Br07_DiscountReducesHallRental(int percentage, double discount, double total)
    {
        var price = EventPricingCalculator.Calculate(4, percentage, []);

        price.MembershipDiscount.Amount.ShouldBe((decimal)discount);
        price.Total.Amount.ShouldBe((decimal)total);
    }

    // ---- BR-08: extras ----

    [Fact]
    public void Calculate_Br08_ExtrasAreSummedAtFrozenUnitCost()
    {
        // 2 TVs (100 each) + projector (150) + coffee break for 40 people (35 each) = 200 + 150 + 1.400
        ExtraServiceLine[] extras =
        [
            Equipment("TV", "Pantalla TV", 100m, 2),
            Equipment("PROJECTOR", "Proyector", 150m),
            Catering("COFFEE_BREAK", "Coffee break", 35m, 40)
        ];

        var price = EventPricingCalculator.Calculate(2, 0, extras);

        price.ExtrasTotal.Amount.ShouldBe(1750m);
        price.Total.Amount.ShouldBe(2750m); // 1.000 rental + 1.750 extras
    }

    [Fact]
    public void Calculate_Total_IsRentalMinusDiscountPlusExtras()
    {
        var price = EventPricingCalculator.Calculate(6, 10, [Equipment("SOUND", "Sistema de sonido", 250m)]);

        // 6 h: 3.000 * 0.8 = 2.400; 10 % = 240; total = 2.400 - 240 + 250
        price.Total.Amount.ShouldBe(2410m);
    }

    [Fact]
    public void Calculate_Rounding_IsTwoDecimalsAwayFromZero()
    {
        var rate = DegressiveRate.Create(Money.Of(333.33m));

        var price = EventPricingCalculator.Calculate(3, 5, [], rate);

        // 333.33 * 3 * 0.9 = 899.991 -> 899.99; 5 % = 45.00 (44.9995) -> 45.00
        price.HallRental.Amount.ShouldBe(899.99m);
        price.MembershipDiscount.Amount.ShouldBe(45.00m);
        price.Total.Amount.ShouldBe(854.99m);
    }

    [Fact]
    public void ExtraServiceLine_LineTotal_IsQuantityTimesUnitCost() =>
        Catering("LUNCH", "Almuerzo ejecutivo", 90m, 35).LineTotal.Amount.ShouldBe(3150m);

    [Fact]
    public void Issue_PriceMatchesSeedQuoteQ1Style()
    {
        // 4 h (09-13), Ana (Flexible, 5 %), 40 people: 1.800 - 90 = 1.710
        var quote = Issue(Slot(9, 13), attendance: 40, discount: 5);

        quote.Price.Total.Amount.ShouldBe(1710m);
    }
}
