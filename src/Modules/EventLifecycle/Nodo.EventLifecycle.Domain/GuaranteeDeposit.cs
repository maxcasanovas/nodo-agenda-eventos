using Nodo.SharedKernel;

namespace Nodo.EventLifecycle.Domain;

public sealed record DepositAccreditation(GuaranteeDeposit Deposit, Money Applied, Money Excess);

/// <summary>
/// Guarantee deposit (BR-14, BR-15). Immutable: <see cref="Accredit"/> returns a new instance.
/// Accredited never exceeds Required; whatever a payment adds beyond that is returned as excess
/// so it can become customer credit without being counted twice.
/// </summary>
public sealed class GuaranteeDeposit : ValueObject
{
    public const int MinPercentage = 20;
    public const int MaxPercentage = 50;

    private readonly string[] _transactionIds;

    // For EF Core only: rebuilds the value from its stored columns. Domain code always goes through Create / Accredit.
    private GuaranteeDeposit()
    {
        Required = null!;
        Accredited = null!;
        _transactionIds = [];
    }

    private GuaranteeDeposit(int percentage, Money required, Money accredited, string[] transactionIds)
    {
        Percentage = percentage;
        Required = required;
        Accredited = accredited;
        _transactionIds = transactionIds;
    }

    public int Percentage { get; }

    public Money Required { get; }

    public Money Accredited { get; }

    public IReadOnlyCollection<string> TransactionIds => Array.AsReadOnly(_transactionIds);

    public bool IsCovered => Accredited.Amount >= Required.Amount;

    public Money Pending => Required.Subtract(Accredited);

    public static GuaranteeDeposit Create(int percentage, Money total)
    {
        if (percentage is < MinPercentage or > MaxPercentage)
        {
            throw new DomainException(InvariantCodes.DepositPercentageOutOfRange,
                $"El depósito de garantía debe ser entre {MinPercentage} % y {MaxPercentage} % del total " +
                $"(se indicó {percentage} %).");
        }

        return new GuaranteeDeposit(
            percentage, total.Percentage(percentage), Money.Of(0m, total.Currency), []);
    }

    public bool HasTransaction(string transactionId) =>
        _transactionIds.Contains(transactionId, StringComparer.Ordinal);

    /// <summary>
    /// Adds a payment. Currency mismatch raises MONEY.CURRENCY_MISMATCH (INV-10).
    /// A transaction already accredited is not applied twice: the same deposit comes back with zero applied.
    /// </summary>
    public DepositAccreditation Accredit(Money payment, string transactionId)
    {
        if (string.IsNullOrWhiteSpace(transactionId))
            throw new DomainException(InvariantCodes.DepositNotAccepted, "El pago requiere un identificador de transacción.");

        if (HasTransaction(transactionId))
            return new DepositAccreditation(this, Money.Of(0m, payment.Currency), Money.Of(0m, payment.Currency));

        if (payment.Amount <= 0)
            throw new DomainException(InvariantCodes.DepositNotAccepted, "El monto del pago debe ser mayor a cero.");

        var newTotal = Accredited.Add(payment); // throws on currency mismatch
        Money applied, excess, newAccredited;
        if (newTotal.Amount > Required.Amount)
        {
            newAccredited = Required;
            applied = Required.Subtract(Accredited);
            excess = newTotal.Subtract(Required);
        }
        else
        {
            newAccredited = newTotal;
            applied = payment;
            excess = Money.Of(0m, payment.Currency);
        }

        var deposit = new GuaranteeDeposit(Percentage, Required, newAccredited, [.. _transactionIds, transactionId]);
        return new DepositAccreditation(deposit, applied, excess);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Percentage;
        yield return Required;
        yield return Accredited;
        foreach (var id in _transactionIds)
            yield return id;
    }
}
