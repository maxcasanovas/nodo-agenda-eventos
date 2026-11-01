using Nodo.SharedKernel;

namespace Nodo.EventLifecycle.Domain;

public enum ExtraServiceType
{
    OwnEquipment,
    ExternalCatering
}

/// <summary>Extra service of an event. Created and changed only through <see cref="HallEvent"/> (INV-13).</summary>
public sealed class ExtraServiceItem : Entity<ExtraServiceItemId>
{
    private ExtraServiceItem() : base(default)
    {
        Code = null!;
        Description = null!;
        UnitCost = null!;
    }

    private ExtraServiceItem(
        ExtraServiceItemId id, ExtraServiceType type, string code, string description, int quantity, Money unitCost)
        : base(id)
    {
        Type = type;
        Code = code;
        Description = description;
        Quantity = quantity;
        UnitCost = unitCost;
    }

    public ExtraServiceType Type { get; private set; }

    public string Code { get; private set; }

    public string Description { get; private set; }

    public int Quantity { get; private set; }

    public Money UnitCost { get; private set; }

    public Money LineTotal => UnitCost.Multiply(Quantity);

    internal static ExtraServiceItem Create(
        ExtraServiceType type, string code, string description, int quantity, Money unitCost)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(description))
            throw new DomainException(InvariantCodes.ExtraServiceInvalid, "El servicio extra requiere código y descripción.");
        if (!Enum.IsDefined(type))
            throw new DomainException(InvariantCodes.ExtraServiceInvalid, "El tipo de servicio extra no es válido.");
        if (quantity < 1)
            throw new DomainException(InvariantCodes.ExtraServiceInvalid,
                $"La cantidad de '{description}' debe ser al menos 1.");

        return new ExtraServiceItem(
            ExtraServiceItemId.New(), type, code.Trim().ToUpperInvariant(), description.Trim(), quantity, unitCost);
    }
}
