namespace Nodo.SharedKernel.UnitTests;

public class MoneyTests
{
    [Fact]
    public void Add_TwoAmounts_ReturnsSum()
    {
        var result = Money.Of(100m).Add(Money.Of(50.25m));

        result.Amount.ShouldBe(150.25m);
        result.Currency.ShouldBe("BOB");
    }

    [Fact]
    public void Subtract_ResultWouldBeNegative_ThrowsNegativeAmount()
    {
        // INV-10
        var ex = Should.Throw<DomainException>(() => Money.Of(10m).Subtract(Money.Of(10.01m)));

        ex.Code.ShouldBe(InvariantCodes.NegativeAmount);
    }

    [Fact]
    public void Subtract_ExactlyZero_IsAllowed()
    {
        // INV-10 boundary
        Money.Of(10m).Subtract(Money.Of(10m)).ShouldBe(Money.Zero);
    }

    [Fact]
    public void Of_NegativeAmount_ThrowsNegativeAmount()
    {
        // INV-10
        var ex = Should.Throw<DomainException>(() => Money.Of(-1m));

        ex.Code.ShouldBe(InvariantCodes.NegativeAmount);
    }

    [Fact]
    public void Add_DifferentCurrencies_ThrowsCurrencyMismatch()
    {
        // INV-10
        var ex = Should.Throw<DomainException>(() => Money.Of(10m, "BOB").Add(Money.Of(10m, "USD")));

        ex.Code.ShouldBe(InvariantCodes.CurrencyMismatch);
    }

    [Theory]
    [InlineData(1000, 30, 300)]
    [InlineData(2800, 50, 1400)]
    [InlineData(1999.99, 20, 400)] // 399.998 -> 400.00
    public void Percentage_ValidPercent_ReturnsRoundedPortion(double total, int percent, double expected)
    {
        Money.Of((decimal)total).Percentage(percent).Amount.ShouldBe((decimal)expected);
    }

    [Theory]
    [InlineData(1.005, 1.01)]  // midpoint rounds away from zero (banker's rounding would give 1.00)
    [InlineData(2.675, 2.68)]
    [InlineData(0.004, 0.00)]
    public void Of_MoreThanTwoDecimals_RoundsAwayFromZero(double input, double expected)
    {
        Money.Of((decimal)input).Amount.ShouldBe((decimal)expected);
    }

    [Fact]
    public void Multiply_Factor_RoundsResult()
    {
        Money.Of(500m).Multiply(0.9m).Amount.ShouldBe(450m);
        Money.Of(33.33m).Multiply(0.5m).Amount.ShouldBe(16.67m); // 16.665 -> 16.67
    }

    [Fact]
    public void Equals_SameAmountAndCurrency_AreEqual()
    {
        Money.Of(10m).ShouldBe(Money.Of(10.00m, "bob"));
        Money.Of(10m).ShouldNotBe(Money.Of(10m, "USD"));
        Money.Of(10m).ShouldNotBe(Money.Of(11m));
    }
}
