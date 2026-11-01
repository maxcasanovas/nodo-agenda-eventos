using Nodo.SharedKernel;

namespace Nodo.HallSchedule.Domain;

/// <summary>Quantity of an own-equipment item reserved by a block.</summary>
public sealed class EquipmentAllocation : ValueObject
{
    private EquipmentAllocation(string code, int quantity)
    {
        Code = code;
        Quantity = quantity;
    }

    public string Code { get; }

    public int Quantity { get; }

    public static EquipmentAllocation Create(string code, int quantity)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException(InvariantCodes.EquipmentUnavailable, "El código del equipo es obligatorio.");
        if (quantity < 1)
            throw new DomainException(InvariantCodes.EquipmentUnavailable,
                $"La cantidad del equipo '{code}' debe ser al menos 1.");

        return new EquipmentAllocation(code.Trim().ToUpperInvariant(), quantity);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Code;
        yield return Quantity;
    }
}
