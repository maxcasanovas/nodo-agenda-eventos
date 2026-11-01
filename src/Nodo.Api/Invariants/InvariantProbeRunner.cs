using Microsoft.EntityFrameworkCore;
using Npgsql;
using Nodo.BuildingBlocks.Application;
using Nodo.EventLifecycle.Infrastructure.Persistence;
using Nodo.HallSchedule.Infrastructure.Persistence;
using Nodo.Quotes.Infrastructure.Persistence;

namespace Nodo.Api.Invariants;

/// <summary>
/// Runs a probe in a scope of its own whose three module DbContexts share one connection and one transaction. The
/// transaction is always rolled back: the probe goes through the real use cases (validators, handlers, aggregates, unit
/// of work, outbox) and still leaves no trace in the demo's data or in the outbox.
/// </summary>
public sealed class InvariantProbeRunner(IServiceScopeFactory scopes, IConfiguration configuration)
{
    /// <exception cref="NotFoundException">The invariant id does not exist.</exception>
    public async Task<ProbeResult> RunAsync(string invariantId, CancellationToken cancellationToken)
    {
        var info = InvariantCatalog.Find(invariantId)
            ?? throw new NotFoundException($"No existe la invariante '{invariantId}'.");
        if (!InvariantProbes.Has(info.Id))
            throw new NotFoundException($"La invariante '{info.Id}' no tiene sonda.");

        await using var scope = scopes.CreateAsyncScope();
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Nodo"));
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var services = scope.ServiceProvider;
            foreach (var context in new DbContext[]
                     {
                         services.GetRequiredService<QuotesDbContext>(),
                         services.GetRequiredService<EventLifecycleDbContext>(),
                         services.GetRequiredService<HallScheduleDbContext>()
                     })
            {
                context.Database.SetDbConnection(connection);
                await context.Database.UseTransactionAsync(transaction, cancellationToken);
            }

            var outcome = await InvariantProbes.RunAsync(info.Id, new ProbeContext(services));
            return new ProbeResult(
                info.Id, outcome.Attempted, outcome.Attempt, outcome.Rejected, outcome.Code, outcome.Message, outcome.StatusCode);
        }
        finally
        {
            await transaction.RollbackAsync(CancellationToken.None); // never commit: probes leave nothing behind
        }
    }
}
