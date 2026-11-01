using Nodo.SharedKernel;

namespace Nodo.Quotes.Domain;

/// <summary>Degressive hourly rate (BR-06): the factor for the total duration applies to all hours.</summary>
public sealed class DegressiveRate : ValueObject
{
    private DegressiveRate(Money baseHourlyRate) => BaseHourlyRate = baseHourlyRate;

    public Money BaseHourlyRate { get; }

    /// <summary>500 Bs per hour [Inventado].</summary>
    public static DegressiveRate Standard { get; } = new(Money.Of(500m));

    public static DegressiveRate Create(Money baseHourlyRate) => new(baseHourlyRate);

    public decimal FactorFor(int hours) => hours switch
    {
        1 or 2 => 1.00m,
        3 or 4 => 0.90m,
        5 or 6 => 0.80m,
        7 or 8 => 0.70m,
        _ => throw new DomainException(InvariantCodes.DurationOutOfRange,
            $"La tarifa solo aplica a eventos de {TimeSlot.MinHours} a {TimeSlot.MaxHours} horas (se indicó {hours}).")
    };

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return BaseHourlyRate;
    }
}
