using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TimeTracker.Core;
using TimeTracker.Core.Abstractions;
using TimeTracker.Core.Events;

namespace TimeTracker.Core.Tests;

public class TrackerServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);

    private sealed class MemoryEventStore : IEventStore
    {
        public List<TrackerEvent> Events { get; } = [];
        public bool FailOnAppend { get; set; }

        public void Append(TrackerEvent trackerEvent)
        {
            if (FailOnAppend)
                throw new IOException("диск недоступен");
            Events.Add(trackerEvent);
        }

        public IReadOnlyList<TrackerEvent> ReadAll() => Events.ToList();
    }

    private sealed class MemoryHeartbeatStore : IHeartbeatStore
    {
        public DateTimeOffset? Value { get; set; }
        public void Write(DateTimeOffset at) => Value = at;
        public DateTimeOffset? Read() => Value;
    }

    private sealed class FakeActivity : IActivityMonitor
    {
        public event Action<DateTimeOffset>? IdleDetected;
        public event Action? InputResumed;
        public void Idle(DateTimeOffset lastInputAt) => IdleDetected?.Invoke(lastInputAt);
        public void Resume() => InputResumed?.Invoke();
    }

    private sealed class FakeSession : ISessionEventsSource
    {
        public event Action? Locked;
        public event Action? Unlocked;
        public event Action? Ending;
        public void Lock() => Locked?.Invoke();
        public void Unlock() => Unlocked?.Invoke();
        public void End() => Ending?.Invoke();
    }

    private sealed class Harness : IAsyncDisposable
    {
        public FakeTimeProvider Time { get; } = new(T0);
        public MemoryEventStore Store { get; } = new();
        public MemoryHeartbeatStore Heartbeat { get; } = new();
        public FakeActivity Activity { get; } = new();
        public FakeSession Session { get; } = new();
        public TrackerService Service { get; private set; } = null!;

        public IEnumerable<StateChanged> Changes => Store.Events.OfType<StateChanged>();

        public async Task StartAsync()
        {
            Time.SetLocalTimeZone(TimeZoneInfo.Utc);
            Service = new TrackerService(
                Store, Heartbeat, Activity, Session, Time,
                Options.Create(new TrackerOptions()), NullLogger<TrackerService>.Instance);
            await Service.StartAsync(CancellationToken.None);
        }

        public async ValueTask DisposeAsync() => await Service.StopAsync(CancellationToken.None);
    }

    private static StateChanged Change(DateTimeOffset at, TimerState from, TimerState to, TransitionReason reason) =>
        new(Guid.NewGuid(), at, at, from, to, reason);

    [Fact]
    public async Task Starts_in_stop_and_user_start_activates()
    {
        await using var h = new Harness();
        await h.StartAsync();
        Assert.Equal(TimerState.Stop, h.Service.State);

        Assert.True(await h.Service.UserStartAsync());

        Assert.Equal(TimerState.Active, h.Service.State);
        var change = Assert.Single(h.Changes);
        Assert.Equal(TimerState.Stop, change.From);
        Assert.Equal(TimerState.Active, change.To);
        Assert.Equal(TransitionReason.UserStart, change.Reason);
        Assert.Equal(T0, change.At);
    }

    [Fact]
    public async Task Invalid_transition_is_ignored_and_not_journaled()
    {
        await using var h = new Harness();
        await h.StartAsync();

        Assert.False(await h.Service.UserStopAsync());

        Assert.Equal(TimerState.Stop, h.Service.State);
        Assert.Empty(h.Store.Events);
    }

    [Fact]
    public async Task Idle_period_is_not_counted_and_transition_is_dated_at_last_input()
    {
        await using var h = new Harness();
        await h.StartAsync();
        await h.Service.UserStartAsync();

        var lastInput = T0.AddMinutes(10);
        h.Time.Advance(TimeSpan.FromMinutes(25));
        h.Activity.Idle(lastInput);
        await h.Service.FlushAsync();

        Assert.Equal(TimerState.Idle, h.Service.State);
        Assert.Equal(TimeSpan.FromMinutes(10), h.Service.GetToday().Total);

        var change = h.Changes.Last();
        Assert.Equal(lastInput, change.At);
        Assert.Equal(T0.AddMinutes(25), change.RecordedAt);
        Assert.Equal(TransitionReason.IdleTimeout, change.Reason);

        // Время простоя не растёт, пока нет ввода.
        h.Time.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(TimeSpan.FromMinutes(10), h.Service.GetToday().Total);
    }

    [Fact]
    public async Task Input_resumes_counting_from_now()
    {
        await using var h = new Harness();
        await h.StartAsync();
        await h.Service.UserStartAsync();
        h.Time.Advance(TimeSpan.FromMinutes(25));
        h.Activity.Idle(T0.AddMinutes(10));
        await h.Service.FlushAsync();

        h.Time.Advance(TimeSpan.FromMinutes(5));
        h.Activity.Resume();
        await h.Service.FlushAsync();
        h.Time.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(TimerState.Active, h.Service.State);
        Assert.Equal(TimeSpan.FromMinutes(11), h.Service.GetToday().Total);
    }

    [Fact]
    public async Task Last_input_before_start_does_not_move_transition_before_start()
    {
        await using var h = new Harness();
        await h.StartAsync();
        await h.Service.UserStartAsync();

        h.Time.Advance(TimeSpan.FromMinutes(1));
        h.Activity.Idle(T0.AddMinutes(-5));
        await h.Service.FlushAsync();

        Assert.Equal(T0, h.Changes.Last().At);
        Assert.Equal(TimeSpan.Zero, h.Service.GetToday().Total);
    }

    [Fact]
    public async Task Lock_then_unlock_ends_in_stop_and_time_does_not_grow()
    {
        await using var h = new Harness();
        await h.StartAsync();
        await h.Service.UserStartAsync();
        h.Time.Advance(TimeSpan.FromMinutes(10));

        h.Session.Lock();
        await h.Service.FlushAsync();
        Assert.Equal(TimerState.Lock, h.Service.State);

        h.Time.Advance(TimeSpan.FromHours(1));
        h.Session.Unlock();
        await h.Service.FlushAsync();
        Assert.Equal(TimerState.Stop, h.Service.State);

        h.Time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(TimeSpan.FromMinutes(10), h.Service.GetToday().Total);
    }

    [Fact]
    public async Task User_start_is_ignored_while_locked()
    {
        await using var h = new Harness();
        await h.StartAsync();
        h.Session.Lock();
        await h.Service.FlushAsync();

        Assert.False(await h.Service.UserStartAsync());
        Assert.Equal(TimerState.Lock, h.Service.State);
    }

    [Fact]
    public async Task Session_ending_is_journaled_before_the_call_returns()
    {
        await using var h = new Harness();
        await h.StartAsync();
        await h.Service.UserStartAsync();

        h.Session.End();

        Assert.Equal(TimerState.Shutdown, h.Service.State);
        Assert.Equal(TransitionReason.SessionEnding, h.Changes.Last().Reason);
    }

    [Fact]
    public async Task Repeated_session_ending_writes_one_event()
    {
        await using var h = new Harness();
        await h.StartAsync();

        h.Session.End();
        h.Session.End();

        Assert.Single(h.Changes);
    }

    [Fact]
    public async Task User_exit_is_journaled_and_next_start_needs_no_recovery()
    {
        await using var h = new Harness();
        await h.StartAsync();
        await h.Service.UserStartAsync();
        h.Time.Advance(TimeSpan.FromMinutes(10));

        await h.Service.UserExitAsync();
        await h.Service.StopAsync(CancellationToken.None);

        Assert.Equal(TimerState.Shutdown, h.Service.State);
        Assert.Equal(TransitionReason.UserExit, h.Changes.Last().Reason);

        h.Time.Advance(TimeSpan.FromHours(1));
        await h.StartAsync();

        Assert.Equal(TimerState.Stop, h.Service.State);
        Assert.Equal(2, h.Changes.Count());
        Assert.Equal(TimeSpan.FromMinutes(10), h.Service.GetToday().Total);
    }

    [Fact]
    public async Task Adjustment_below_zero_is_rejected_and_not_journaled()
    {
        await using var h = new Harness();
        await h.StartAsync();
        await h.Service.UserStartAsync();
        h.Time.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(AdjustResult.BelowZero, await h.Service.AdjustAsync(-11, null));

        Assert.Empty(h.Store.Events.OfType<TimeAdjusted>());
        Assert.Equal(TimeSpan.FromMinutes(10), h.Service.GetToday().Total);
    }

    [Fact]
    public async Task Adjustment_is_journaled_with_comment_for_current_day()
    {
        await using var h = new Harness();
        await h.StartAsync();
        await h.Service.UserStartAsync();
        h.Time.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(AdjustResult.Applied, await h.Service.AdjustAsync(-10, "обед"));

        var adjustment = Assert.Single(h.Store.Events.OfType<TimeAdjusted>());
        Assert.Equal(new DateOnly(2026, 3, 10), adjustment.Day);
        Assert.Equal(-10, adjustment.Minutes);
        Assert.Equal("обед", adjustment.Comment);
        Assert.Equal(TimeSpan.Zero, h.Service.GetToday().Total);
    }

    [Fact]
    public async Task Zero_adjustment_is_rejected()
    {
        await using var h = new Harness();
        await h.StartAsync();

        Assert.Equal(AdjustResult.InvalidMinutes, await h.Service.AdjustAsync(0, null));
    }

    [Theory]
    [InlineData(TimerState.Active)]
    [InlineData(TimerState.Idle)]
    public async Task Recovery_adds_shutdown_at_last_heartbeat(TimerState lastState)
    {
        await using var h = new Harness();
        h.Store.Events.Add(Change(T0, TimerState.Stop, lastState, TransitionReason.UserStart));
        h.Heartbeat.Value = T0.AddMinutes(20);
        h.Time.Advance(TimeSpan.FromHours(2));

        await h.StartAsync();

        var recovery = h.Changes.Last();
        Assert.Equal(lastState, recovery.From);
        Assert.Equal(TimerState.Shutdown, recovery.To);
        Assert.Equal(TransitionReason.RecoveredAfterCrash, recovery.Reason);
        Assert.Equal(T0.AddMinutes(20), recovery.At);
        Assert.Equal(T0.AddHours(2), recovery.RecordedAt);
        Assert.Equal(TimerState.Stop, h.Service.State);
    }

    [Fact]
    public async Task Recovery_counts_active_time_only_up_to_heartbeat()
    {
        await using var h = new Harness();
        h.Store.Events.Add(Change(T0, TimerState.Stop, TimerState.Active, TransitionReason.UserStart));
        h.Heartbeat.Value = T0.AddMinutes(20);
        h.Time.Advance(TimeSpan.FromHours(2));

        await h.StartAsync();

        Assert.Equal(TimeSpan.FromMinutes(20), h.Service.GetToday().Total);
    }

    [Fact]
    public async Task Recovery_without_or_with_stale_heartbeat_uses_last_event_time()
    {
        await using var h = new Harness();
        h.Store.Events.Add(Change(T0, TimerState.Stop, TimerState.Active, TransitionReason.UserStart));
        h.Heartbeat.Value = T0.AddMinutes(-30);
        h.Time.Advance(TimeSpan.FromHours(2));

        await h.StartAsync();

        Assert.Equal(T0, h.Changes.Last().At);
        Assert.Equal(TimeSpan.Zero, h.Service.GetToday().Total);
    }

    [Theory]
    [InlineData(TimerState.Stop)]
    [InlineData(TimerState.Lock)]
    [InlineData(TimerState.Shutdown)]
    public async Task No_recovery_when_journal_does_not_end_in_running_state(TimerState lastState)
    {
        await using var h = new Harness();
        h.Store.Events.Add(Change(T0, TimerState.Active, lastState, TransitionReason.UserStop));

        await h.StartAsync();

        Assert.Single(h.Changes);
        Assert.Equal(TimerState.Stop, h.Service.State);
    }

    [Fact]
    public async Task Recovery_looks_past_adjustments_at_the_end_of_the_journal()
    {
        await using var h = new Harness();
        h.Store.Events.Add(Change(T0, TimerState.Stop, TimerState.Active, TransitionReason.UserStart));
        h.Store.Events.Add(new TimeAdjusted(Guid.NewGuid(), T0, new DateOnly(2026, 3, 10), 5, null));
        h.Time.Advance(TimeSpan.FromHours(1));

        await h.StartAsync();

        Assert.Equal(TransitionReason.RecoveredAfterCrash, h.Changes.Last().Reason);
    }

    [Fact]
    public async Task Heartbeat_is_written_on_start()
    {
        await using var h = new Harness();
        await h.StartAsync();

        Assert.Equal(T0, h.Heartbeat.Value);
    }

    [Fact]
    public async Task Failed_append_keeps_state_and_surfaces_the_error()
    {
        await using var h = new Harness();
        await h.StartAsync();
        h.Store.FailOnAppend = true;

        await Assert.ThrowsAsync<IOException>(() => h.Service.UserStartAsync());

        Assert.Equal(TimerState.Stop, h.Service.State);
    }
}
