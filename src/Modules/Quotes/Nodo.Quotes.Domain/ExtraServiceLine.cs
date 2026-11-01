using Nodo.SharedKernel;

namespace Nodo.Quotes.Domain;

public enum ExtraServiceType
{
    OwnEquipment,
    ExternalCatering
}

/// <summary>A priced extra service frozen in the quote (BR-08). Unit cost is captured at quoting time.</summary>
public sealed class ExtraServiceLine : ValueObject
{
    // For EF Core only: rebuilds the value from its stored columns. Domain code always goes through Create.
    private ExtraServiceLine()
    {
        Code = null!;
        Name = null!;
        UnitCost = null!;
    }

    private ExtraServiceLine(string code, string name, ExtraServiceType type, int quantity, Money unitCost)
    {
        Code = code;
        Name = name;
        Type = type;
        Quantity = quantity;
        UnitCost = unitCost;
    }

    public string Code { get; }

    public string Name { get; }

    public ExtraServiceType Type { get; }

    public int Quantity { get; }

    public Money UnitCost { get; }

    public Money LineTotal => UnitCost.Multiply(Quantity);

    public static ExtraServiceLine Create(string code, string name, ExtraServiceType type, int quantity, Money unitCost)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            throw new DomainException(InvariantCodes.ExtraServiceInvalid, "El servicio extra requiere código y nombre.");
        if (!Enum.IsDefined(type))
            throw new DomainException(InvariantCodes.ExtraServiceInvalid, "El tipo de servicio extra no es válido.");
        if (quantity < 1)
            throw new DomainException(InvariantCodes.ExtraServiceInvalid,
                $"La cantidad de '{name}' debe ser al menos 1.");

        return new ExtraServiceLine(code.Trim().ToUpperInvariant(), name.Trim(), type, quantity, unitCost);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Code;
        yield return Name;
        yield return Type;
        yield return Quantity;
        yield return UnitCost;
    }
}
