using Nodo.SharedKernel;

namespace Nodo.EventLifecycle.Domain;

public enum CreditMovementType
{
    Granted,
    Applied
}

/// <summary>Entry in the credit ledger.</summary>
public sealed class CreditMovement : ValueObject
{
    // For EF Core only: rebuilds the value from its stored columns.
    private CreditMovement()
    {
        Amount = null!;
        Reason = null!;
    }

    private CreditMovement(CreditMovementType type, Money amount, HallEventId eventId, string reason, DateTime occurredAtUtc)
    {
        Type = type;
        Amount = amount;
        EventId = eventId;
        Reason = reason;
        OccurredAtUtc = occurredAtUtc;
    }

    public CreditMovementType Type { get; }

    public Money Amount { get; }

    public HallEventId EventId { get; }

    public string Reason { get; }

    public DateTime OccurredAtUtc { get; }

    internal static CreditMovement Create(
        CreditMovementType type, Money amount, HallEventId eventId, string reason, DateTime occurredAtUtc) =>
        new(type, amount, eventId, reason, occurredAtUtc);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Type;
        yield return Amount;
        yield return EventId;
        yield return Reason;
        yield return OccurredAtUtc;
    }
}

/// <summary>
/// A customer's credit (BR-22). It only grows through <see cref="Grant"/> and only shrinks through
/// <see cref="Apply"/>; there is deliberately no withdraw or refund operation (INV-22).
/// </summary>
public sealed class CustomerCredit : AggregateRoot<CustomerId>
{
    private readonly List<CreditMovement> _movements = [];

    private CustomerCredit() : base(default)
    {
        Balance = null!;
    }

    private CustomerCredit(CustomerId customerId) : base(customerId)
    {
        Balance = Money.Zero;
    }

    public Money Balance { get; private set; }

    public IReadOnlyCollection<CreditMovement> Movements => _movements.AsReadOnly();

    public static CustomerCredit Open(CustomerId customerId) => new(customerId);

    /// <summary>Adds credit (cancellation, expiry with partial payment, deposit excess). A zero amount is ignored.</summary>
    public void Grant(Money amount, HallEventId eventId, string reason, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (amount.Amount == 0)
            return;

        var newBalance = Balance.Add(amount); // throws on currency mismatch

        Balance = newBalance;
        _movements.Add(CreditMovement.Create(CreditMovementType.Granted, amount, eventId, reason.Trim(), nowUtc));
        RaiseDomainEvent(new CustomerCreditGranted(Id, eventId, amount, newBalance, nowUtc));
    }

    /// <summary>
    /// Uses credit up to <paramref name="maxAmount"/> and returns what was actually applied: asking for more
    /// than the balance applies only the balance (INV-21). Fails when there is nothing to apply.
    /// </summary>
    public Money Apply(Money maxAmount, HallEventId eventId, DateTime nowUtc)
    {
        if (maxAmount.Amount <= 0 || Balance.Amount <= 0)
        {
            throw new DomainException(InvariantCodes.CreditInsufficient,
                Balance.Amount <= 0
                    ? "El cliente no tiene saldo a favor disponible."
                    : "El monto a aplicar debe ser mayor a cero.");
        }

        var applied = maxAmount.Amount <= Balance.Amount ? maxAmount : Balance;
        var newBalance = Balance.Subtract(applied); // throws on currency mismatch

        Balance = newBalance;
        _movements.Add(CreditMovement.Create(CreditMovementType.Applied, applied, eventId, "Aplicado a reprogramación.", nowUtc));
        RaiseDomainEvent(new CustomerCreditApplied(Id, eventId, applied, newBalance, nowUtc));
        return applied;
    }
}
