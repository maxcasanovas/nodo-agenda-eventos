using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Nodo.BuildingBlocks.Infrastructure.Jobs;

/// <summary>A background job that can also be triggered on demand (the demo's "run jobs now").</summary>
public interface IRunnableJob
{
    string Name { get; }

    /// <returns>How many items the run changed.</returns>
    Task<int> RunOnceAsync(CancellationToken cancellationToken = default);
}

/// <summary>Runs a unit of work every 30 s in its own DI scope. Manual and scheduled runs never overlap.</summary>
public abstract class PeriodicJob(IServiceScopeFactory scopeFactory, ILogger logger) : BackgroundService, IRunnableJob
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public abstract string Name { get; }

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var scope = scopeFactory.CreateScope();
            return await RunAsync(scope.ServiceProvider, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    protected abstract Task<int> RunAsync(IServiceProvider scopedServices, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                var changed = await RunOnceAsync(stoppingToken);
                if (changed > 0)
                    logger.LogInformation("{Job} changed {Count} item(s)", Name, changed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "{Job} failed; it will run again on the next tick", Name);
            }
        }
        while (await WaitNextTickAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitNextTickAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
