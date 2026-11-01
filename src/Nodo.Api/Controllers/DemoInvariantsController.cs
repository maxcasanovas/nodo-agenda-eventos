using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nodo.Api.Invariants;

namespace Nodo.Api.Controllers;

[ApiController]
[Route("api/demo/invariants")]
[Produces("application/json")]
[Tags("Demo")]
public sealed class DemoInvariantsController(InvariantProbeRunner runner) : ControllerBase
{
    public sealed record InvariantEntry(
        string Id, string Description, string Owner, string Code, bool DatabaseEnforced, bool ByDesign, bool HasProbe);

    [HttpGet]
    [EndpointSummary("Catálogo de invariantes")]
    [EndpointDescription("Las 22 invariantes del modelo: descripción en español, agregado dueño, `code` que devuelve la API en el 422, si PostgreSQL también la refuerza y si tiene sonda.")]
    [ProducesResponseType<IReadOnlyList<InvariantEntry>>(StatusCodes.Status200OK)]
    public IReadOnlyList<InvariantEntry> Catalog() => InvariantCatalog.All
        .Select(i => new InvariantEntry(i.Id, i.Description, i.Owner, i.Code, i.DatabaseEnforced, i.ByDesign, InvariantProbes.Has(i.Id)))
        .ToList();

    [HttpPost("{id}/probe")]
    [EndpointSummary("Sonda de una invariante")]
    [EndpointDescription(
        "Intenta violar la invariante **a través del caso de uso real** y devuelve lo que la API respondería: `rejected`, `code`, `message` y `statusCode`. " +
        "La sonda crea sus propios datos efímeros y corre dentro de una transacción que siempre se revierte, así que no altera la demo ni deja eventos en el outbox. " +
        "Las invariantes que se mantienen por diseño (no existe la operación) devuelven `attempted: false` con la explicación.")]
    [ProducesResponseType<ProbeResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ProbeResult> Probe(string id, CancellationToken cancellationToken) =>
        await runner.RunAsync(id, cancellationToken);
}