namespace Nodo.SharedKernel;

/// <summary>Monetary amount. Never negative, always rounded to 2 decimals (AwayFromZero).</summary>
public sealed record Money
{
    public const string DefaultCurrency = "BOB";

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Zero => new(0m, DefaultCurrency);

    public static Money Of(decimal amount, string currency = DefaultCurrency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            throw new DomainException(InvariantCodes.CurrencyMismatch, "La moneda es obligatoria.");

        var rounded = Round(amount);
        if (rounded < 0)
            throw new DomainException(InvariantCodes.NegativeAmount, "El monto no puede ser negativo.");

        return new Money(rounded, currency.Trim().ToUpperInvariant());
    }

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return Of(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return Of(Amount - other.Amount, Currency);
    }

    public Money Multiply(decimal factor) => Of(Amount * factor, Currency);

    /// <summary>Returns <paramref name="percent"/>% of this amount.</summary>
    public Money Percentage(int percent) => Of(Amount * percent / 100m, Currency);

    private void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
            throw new DomainException(
                InvariantCodes.CurrencyMismatch,
                $"No se pueden combinar montos en monedas distintas ({Currency} y {other.Currency}).");
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static Money operator +(Money left, Money right) => left.Add(right);

    public static Money operator -(Money left, Money right) => left.Subtract(right);

    public override string ToString() => $"{Amount:0.00} {Currency}";
}
