namespace Nodo.EventLifecycle.Domain;

public readonly record struct HallEventId(Guid Value)
{
    public static HallEventId New() => new(Guid.NewGuid());
}

public readonly record struct ExtraServiceItemId(Guid Value)
{
    public static ExtraServiceItemId New() => new(Guid.NewGuid());
}

public readonly record struct QuoteId(Guid Value);

public readonly record struct CustomerId(Guid Value);

public readonly record struct HallId(Guid Value);
