namespace Nodo.SharedKernel;

/// <summary>Invariant codes owned by the shared kernel (business-rules.md §9).</summary>
public static class InvariantCodes
{
    // INV-10
    public const string CurrencyMismatch = "MONEY.CURRENCY_MISMATCH";
    public const string NegativeAmount = "MONEY.NEGATIVE_AMOUNT";
}
