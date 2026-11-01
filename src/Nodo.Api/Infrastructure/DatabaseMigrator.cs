using Microsoft.EntityFrameworkCore;
using Nodo.EventLifecycle.Infrastructure.Persistence;
using Nodo.HallSchedule.Infrastructure.Persistence;
using Nodo.Quotes.Infrastructure.Persistence;

namespace Nodo.Api.Infrastructure;

public static class DatabaseMigrator
{
    private const int MaxAttempts = 15;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Applies the pending migrations of the three modules (HallSchedule, Quotes, EventLifecycle), retrying while
    /// PostgreSQL finishes starting. Each module keeps its history table inside its own schema.
    /// </summary>
    public static async Task MigrateDatabasesAsync(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseMigrator");

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var scope = app.Services.CreateScope();
                await MigrateAsync<HallScheduleDbContext>(scope, logger);
                await MigrateAsync<QuotesDbContext>(scope, logger);
                await MigrateAsync<EventLifecycleDbContext>(scope, logger);
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                logger.LogWarning("Database not ready (attempt {Attempt}/{Max}): {Message}", attempt, MaxAttempts, ex.Message);
                await Task.Delay(RetryDelay);
            }
        }
    }

    private static async Task MigrateAsync<TContext>(IServiceScope scope, ILogger logger)
        where TContext : DbContext
    {
        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        await context.Database.MigrateAsync();
        logger.LogInformation("{Context} is up to date", typeof(TContext).Name);
    }
}
