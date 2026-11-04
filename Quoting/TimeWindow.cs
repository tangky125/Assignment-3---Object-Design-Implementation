namespace Dms.Domain;

/// <summary>
/// A start and end time (A2 data holder TimeWindow), used for pickup and delivery windows.
/// It refuses an end that is not after the start, so no class can hold an empty window.
/// </summary>
public sealed record TimeWindow
{
    /// <summary>Creates a window.</summary>
    /// <param name="start">When the window opens.</param>
    /// <param name="end">When the window closes; must be after <paramref name="start"/>.</param>
    public TimeWindow(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start)
        {
            throw new ArgumentException("A window must end after it starts.");
        }

        Start = start;
        End = end;
    }

    /// <summary>When the window opens.</summary>
    public DateTimeOffset Start { get; }

    /// <summary>When the window closes.</summary>
    public DateTimeOffset End { get; }

    /// <summary>How long the window is open.</summary>
    public TimeSpan Length => End - Start;

    /// <summary>True when the two windows share any time.</summary>
    public bool Overlaps(TimeWindow other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Start < other.End && other.Start < End;
    }
}
