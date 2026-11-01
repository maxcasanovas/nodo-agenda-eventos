namespace Nodo.Quotes.Domain;

public readonly record struct QuoteId(Guid Value)
{
    public static QuoteId New() => new(Guid.NewGuid());
}

public readonly record struct CustomerId(Guid Value);

public readonly record struct HallId(Guid Value);
