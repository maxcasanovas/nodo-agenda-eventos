using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Nodo.BuildingBlocks.Application;

namespace Nodo.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Turns a PostgreSQL constraint violation into a <see cref="ConflictException"/> carrying the invariant's code.
/// Constraints are named after their invariant (ck_hall_events_inv04_attendance), so the INV id is read from the name.
/// The database is defense in depth: the domain normally rejects the same input first with a 422.
/// </summary>
public sealed partial class InvariantConstraintMap(IReadOnlyDictionary<string, string> codesByInvariantId)
{
    private const string CheckViolation = "23514";
    private const string ExclusionViolation = "23P01";
    private const string UniqueViolation = "23505";

    /// <param name="codesByInvariantId">Keys like "INV-04", values like "EVENT.ATTENDANCE_OUT_OF_RANGE".</param>
    public static InvariantConstraintMap Of(params (string InvariantId, string Code)[] codes) =>
        new(codes.ToDictionary(c => c.InvariantId, c => c.Code));

    /// <returns>The conflict to throw, or null when the error is not a constraint violation.</returns>
    public ConflictException? Translate(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgres ? Translate(postgres) : null;

    public ConflictException? Translate(PostgresException postgres)
    {
        if (postgres.SqlState is not (CheckViolation or ExclusionViolation or UniqueViolation))
            return null;

        var code = CodeOf(postgres.ConstraintName);
        var message = code is null
            ? "La operación viola una restricción de la base de datos."
            : $"La base de datos rechazó la operación porque viola una regla del negocio ({code}).";
        return new ConflictException(message, code);
    }

    public string? CodeOf(string? constraintName)
    {
        if (constraintName is null)
            return null;

        var match = InvariantId().Match(constraintName);
        return match.Success && codesByInvariantId.TryGetValue($"INV-{match.Groups[1].Value}", out var code) ? code : null;
    }

    [GeneratedRegex(@"_inv(\d{2})_", RegexOptions.IgnoreCase)]
    private static partial Regex InvariantId();
}
