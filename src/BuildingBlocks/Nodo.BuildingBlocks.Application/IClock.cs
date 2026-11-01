namespace Nodo.BuildingBlocks.Application;

/// <summary>The only source of "now" for the application layer. The domain receives time as a parameter.</summary>
public interface IClock
{
    DateTime UtcNow { get; }

    /// <summary>Current date in the hall's local time zone (America/La_Paz).</summary>
    DateOnly TodayLocal { get; }

    DateTime LocalNow { get; }
}
