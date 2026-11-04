using TimeTracker.Core.Events;

namespace TimeTracker.Core;

public sealed record DaySummary(DateOnly Day, TimeSpan Tracked, long AdjustedMinutes, TimeSpan Total, bool ClampedToZero);

public static class SessionCalculator
{
    public static DateOnly DayOf(DateTimeOffset at, TimeZoneInfo timeZone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, timeZone).DateTime);

    public static DaySummary Calculate(
        IEnumerable<TrackerEvent> events, DateOnly day, DateTimeOffset now, TimeZoneInfo timeZone)
    {
        var dayStart = StartOfDay(day, timeZone);
        var dayEnd = StartOfDay(day.AddDays(1), timeZone);

        var tracked = TimeSpan.Zero;
        long adjustedMinutes = 0;
        DateTimeOffset? activeSince = null;

        foreach (var trackerEvent in events)
        {
            switch (trackerEvent)
            {
                case StateChanged change:
                    if (activeSince is { } start && change.To != TimerState.Active)
                    {
                        tracked += Overlap(start, change.At, dayStart, dayEnd);
                        activeSince = null;
                    }
                    if (change.To == TimerState.Active)
                        activeSince ??= change.At;
                    break;

                case TimeAdjusted adjustment when adjustment.Day == day:
                    adjustedMinutes += adjustment.Minutes;
                    break;
            }
        }

        if (activeSince is { } openStart)
            tracked += Overlap(openStart, now, dayStart, dayEnd);

        var raw = tracked + TimeSpan.FromMinutes(adjustedMinutes);
        var clamped = raw < TimeSpan.Zero;
        return new DaySummary(day, tracked, adjustedMinutes, clamped ? TimeSpan.Zero : raw, clamped);
    }

    private static TimeSpan Overlap(DateTimeOffset start, DateTimeOffset end, DateTimeOffset rangeStart, DateTimeOffset rangeEnd)
    {
        var from = start > rangeStart ? start : rangeStart;
        var to = end < rangeEnd ? end : rangeEnd;
        return to > from ? to - from : TimeSpan.Zero;
    }

    private static DateTimeOffset StartOfDay(DateOnly day, TimeZoneInfo timeZone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        // В некоторых поясах перевод часов приходится на полночь, и такого момента не существует.
        while (timeZone.IsInvalidTime(local))
            local = local.AddMinutes(30);

        return new DateTimeOffset(local, timeZone.GetUtcOffset(local));
    }
}
