using Nodo.BuildingBlocks.Application;
using Nodo.SharedKernel;

namespace Nodo.BuildingBlocks.Infrastructure;

/// <summary>
/// Real clock plus an accumulable offset so the demo can "travel" in time (expire quotes, start events...).
/// The only place in the solution allowed to read <see cref="DateTime.UtcNow"/>.
/// </summary>
public sealed class DemoClock : IClock
{
    private long _offsetTicks;

    public TimeSpan Offset => TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));

    public DateTime UtcNow => DateTime.UtcNow + Offset;

    public DateTime LocalNow => NodoTimeZone.ToLocal(UtcNow);

    public DateOnly TodayLocal => DateOnly.FromDateTime(LocalNow);

    public void Advance(TimeSpan amount) => Interlocked.Add(ref _offsetTicks, amount.Ticks);

    public void Reset() => Interlocked.Exchange(ref _offsetTicks, 0);
}
