using FluentValidation;
using Nodo.BuildingBlocks.Application;
using Nodo.SharedKernel;

namespace Nodo.Api.Infrastructure;

/// <summary>An exception seen as an HTTP problem. <see cref="Code"/> is the invariant code when there is one.</summary>
public sealed record ProblemInfo(
    int Status, string Title, string Detail, string? Code, IReadOnlyDictionary<string, string[]>? Errors);

/// <summary>
/// One place that decides how a failure is shown over HTTP: 400 shape, 404 missing, 409 concurrency or database
/// constraint, 422 business rule. Shared by the global exception handler and the invariant probes, so a probe reports
/// exactly what a real request would have received.
/// </summary>
public static class ProblemMapping
{
    /// <returns>The problem, or null for an unexpected exception (which stays a 500).</returns>
    public static ProblemInfo? Map(Exception exception) => exception switch
    {
        DomainException ex => new(StatusCodes.Status422UnprocessableEntity, "Regla de dominio violada", ex.Message, ex.Code, null),
        ValidationException ex => new(
            StatusCodes.Status400BadRequest, "Solicitud inválida", "Uno o más campos no son válidos.", null,
            ex.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())),
        NotFoundException ex => new(StatusCodes.Status404NotFound, "No encontrado", ex.Message, null, null),
        ConflictException ex => new(StatusCodes.Status409Conflict, "Conflicto", ex.Message, ex.Code, null),
        _ => null
    };
}
