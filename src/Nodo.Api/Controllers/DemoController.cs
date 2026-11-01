using Microsoft.AspNetCore.Mvc;
using Nodo.BuildingBlocks.Infrastructure;
using Nodo.BuildingBlocks.Infrastructure.Jobs;
using Nodo.BuildingBlocks.Infrastructure.Messaging;

namespace Nodo.Api.Controllers;

/// <summary>Demo-only controls: the virtual clock, the expiration jobs and the integration event log.</summary>
[ApiController]
[Route("api/demo")]
[Tags("Demo")]
public sealed class DemoController(DemoClock clock, IEnumerable<IRunnableJob> jobs, IntegrationEventLog eventLog)
    : ControllerBase
{
    public sealed record ClockResponse(DateTime UtcNow, DateTime LocalNow, DateOnly TodayLocal, double OffsetHours);

    public sealed record AdvanceClockRequest(int Days = 0, int Hours = 0);

    public sealed record JobResult(string Job, int Changed);

    [HttpGet("clock")]
    [EndpointSummary("Reloj de la demo")]
    [EndpointDescription("La hora actual de la demo (la real más el desplazamiento acumulado) en UTC y en hora local del salón.")]
    public ClockResponse Get() => Snapshot();

    [HttpPost("clock/advance")]
    [EndpointSummary("Adelanta el reloj")]
    [EndpointDescription("Suma días y horas al reloj de la demo. Sirve para vencer cotizaciones, plazos de depósito o iniciar un evento. Después ejecuta ` POST /api/demo/jobs/run `` para ver las expiraciones.")]
    public ClockResponse Advance(AdvanceClockRequest request)
    {
        clock.Advance(TimeSpan.FromDays(request.Days) + TimeSpan.FromHours(request.Hours));
        return Snapshot();
    }

    [HttpPost("clock/reset")]
    [EndpointSummary("Restablece el reloj")]
    [EndpointDescription("Vuelve a la hora real.")]
    public ClockResponse Reset()
    {
        clock.Reset();
        return Snapshot();
    }

    /// <summary>Runs the expiration jobs now (useful right after advancing the clock). Consequences travel through the outbox.</summary>
    [HttpPost("jobs/run")]
    [EndpointSummary("Ejecuta ahora los jobs de expiración")]
    [EndpointDescription("Vence cotizaciones (96 h), solicitudes sin decisión y depósitos sin pagar. Las consecuencias en otros módulos, como liberar el bloqueo, llegan unos segundos después a través del outbox.")]
    public async Task<IReadOnlyCollection<JobResult>> RunJobs(CancellationToken cancellationToken)
    {
        var results = new List<JobResult>();
        foreach (var job in jobs)
            results.Add(new JobResult(job.Name, await job.RunOnceAsync(cancellationToken)));
        return results;
    }

    /// <summary>The most recent integration events of all modules, with their delivery state.</summary>
    [HttpGet("integration-events")]
    [EndpointSummary("Eventos de integración publicados")]
    [EndpointDescription("Los más recientes primero, de los tres módulos, con su estado de entrega (` processedOnUtc `, `attempts`, `error`).")]
    public async Task<IReadOnlyCollection<IntegrationEventLogEntry>> Events(
        [FromQuery] int limit = 50, CancellationToken cancellationToken = default) =>
        await eventLog.GetRecentAsync(limit, cancellationToken);

    private ClockResponse Snapshot() =>
        new(clock.UtcNow, clock.LocalNow, clock.TodayLocal, clock.Offset.TotalHours);
}
