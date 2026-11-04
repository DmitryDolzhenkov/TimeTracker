using TimeTracker.Core;
using TimeTracker.Core.Events;

namespace TimeTracker.Core.Tests;

public class SessionCalculatorTests
{
    private static readonly TimeZoneInfo Plus3 =
        TimeZoneInfo.CreateCustomTimeZone("Test+3", TimeSpan.FromHours(3), "Test+3", "Test+3");

    private static DateTimeOffset Local(int day, int hour, int minute = 0) =>
        new(2026, 3, day, hour, minute, 0, TimeSpan.FromHours(3));

    private static StateChanged Change(DateTimeOffset at, TimerState from, TimerState to, TransitionReason reason) =>
        new(Guid.NewGuid(), at.ToUniversalTime(), at.ToUniversalTime(), from, to, reason);

    private static TrackerEvent Start(DateTimeOffset at) => Change(at, TimerState.Stop, TimerState.Active, TransitionReason.UserStart);

    private static TrackerEvent Stop(DateTimeOffset at) => Change(at, TimerState.Active, TimerState.Stop, TransitionReason.UserStop);

    private static TrackerEvent Adjust(DateOnly day, int minutes) =>
        new TimeAdjusted(Guid.NewGuid(), Local(day.Day, 12), day, minutes, null);

    [Fact]
    public void Open_active_interval_counts_until_now()
    {
        var summary = SessionCalculator.Calculate([Start(Local(10, 9))], new DateOnly(2026, 3, 10), Local(10, 9, 30), Plus3);

        Assert.Equal(TimeSpan.FromMinutes(30), summary.Total);
    }

    [Fact]
    public void Session_across_midnight_is_split_between_local_days()
    {
        TrackerEvent[] events = [Start(Local(10, 23)), Stop(Local(11, 1))];
        var now = Local(11, 12);

        Assert.Equal(TimeSpan.FromHours(1), SessionCalculator.Calculate(events, new DateOnly(2026, 3, 10), now, Plus3).Total);
        Assert.Equal(TimeSpan.FromHours(1), SessionCalculator.Calculate(events, new DateOnly(2026, 3, 11), now, Plus3).Total);
        Assert.Equal(TimeSpan.Zero, SessionCalculator.Calculate(events, new DateOnly(2026, 3, 12), now, Plus3).Total);
    }

    [Fact]
    public void Non_active_states_are_not_counted()
    {
        TrackerEvent[] events =
        [
            Start(Local(10, 9)),
            Change(Local(10, 10), TimerState.Active, TimerState.Idle, TransitionReason.IdleTimeout),
            Change(Local(10, 11), TimerState.Idle, TimerState.Active, TransitionReason.InputResumed),
            Change(Local(10, 12), TimerState.Active, TimerState.Lock, TransitionReason.SessionLock),
            Change(Local(10, 13), TimerState.Lock, TimerState.Stop, TransitionReason.SessionUnlock)
        ];

        var summary = SessionCalculator.Calculate(events, new DateOnly(2026, 3, 10), Local(10, 20), Plus3);

        Assert.Equal(TimeSpan.FromHours(2), summary.Total);
    }

    [Fact]
    public void Adjustments_apply_only_to_their_day()
    {
        var day = new DateOnly(2026, 3, 10);
        TrackerEvent[] events = [Start(Local(10, 9)), Stop(Local(10, 10)), Adjust(day, 15), Adjust(day.AddDays(1), 100)];

        var summary = SessionCalculator.Calculate(events, day, Local(10, 20), Plus3);

        Assert.Equal(TimeSpan.FromMinutes(75), summary.Total);
        Assert.Equal(TimeSpan.FromHours(1), summary.Tracked);
    }

    [Fact]
    public void Negative_total_is_clamped_to_zero_and_flagged()
    {
        var day = new DateOnly(2026, 3, 10);

        var summary = SessionCalculator.Calculate([Adjust(day, -5)], day, Local(10, 20), Plus3);

        Assert.Equal(TimeSpan.Zero, summary.Total);
        Assert.True(summary.ClampedToZero);
    }

    [Fact]
    public void DayOf_uses_local_time_zone()
    {
        // 22:00 UTC 10 марта это уже 01:00 11 марта при UTC+3.
        var at = new DateTimeOffset(2026, 3, 10, 22, 0, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 3, 11), SessionCalculator.DayOf(at, Plus3));
    }
}
