using Nodo.BuildingBlocks.Application;
using Nodo.SharedKernel;

namespace Nodo.BuildingBlocks.Infrastructure;

/// <summary>An extra offset that only one DI scope sees (registered scoped). The invariant probes use it to travel in time.</summary>
public sealed class ClockOffset
{
    public TimeSpan Extra { get; set; }
}

/// <summary>
/// The clock use cases see: the demo clock (real time + the accumulated demo offset) plus the scope's own
/// <see cref="ClockOffset"/>, which is zero except inside a probe. Never reads the system time itself.
/// </summary>
public sealed class ScopedClock(DemoClock demoClock, ClockOffset offset) : IClock
{
    public DateTime UtcNow => demoClock.UtcNow + offset.Extra;

    public DateTime LocalNow => NodoTimeZone.ToLocal(UtcNow);

    public DateOnly TodayLocal => DateOnly.FromDateTime(LocalNow);
}