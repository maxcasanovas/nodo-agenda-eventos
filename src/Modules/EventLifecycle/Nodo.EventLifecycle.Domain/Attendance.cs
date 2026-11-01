using Nodo.SharedKernel;

namespace Nodo.EventLifecycle.Domain;

/// <summary>Expected attendance (INV-04 / BR-04): integer between 1 and 80.</summary>
public sealed class Attendance : ValueObject
{
    public const int Min = 1;
    public const int Max = 80;

    private Attendance(int value) => Value = value;

    public int Value { get; }

    public static Attendance Create(int value)
    {
        if (value is < Min or > Max)
            throw new DomainException(InvariantCodes.AttendanceOutOfRange,
                $"El aforo debe estar entre {Min} y {Max} personas (se indicó {value}).");

        return new Attendance(value);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
