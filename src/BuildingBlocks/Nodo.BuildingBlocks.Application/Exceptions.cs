namespace Nodo.BuildingBlocks.Application;

/// <summary>Maps to HTTP 404.</summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>
/// Maps to HTTP 409 (optimistic concurrency, overlap detected only by the database).
/// <see cref="Code"/> is the invariant code (e.g. SCHEDULE.SLOT_OVERLAP) when a database constraint is the cause.
/// </summary>
public sealed class ConflictException(string message, string? code = null) : Exception(message)
{
    public string? Code { get; } = code;
}
