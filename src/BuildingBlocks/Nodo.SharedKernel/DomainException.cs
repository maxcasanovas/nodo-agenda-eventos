namespace Nodo.SharedKernel;

/// <summary>
/// Violation of a business invariant. <see cref="Code"/> is a stable English identifier
/// (see each module's InvariantCodes); the message is Spanish and shown to the user.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string code, string message) : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public string Code { get; }
}
