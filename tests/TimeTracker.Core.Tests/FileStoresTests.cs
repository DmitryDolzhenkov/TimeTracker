using Microsoft.Extensions.Logging.Abstractions;
using TimeTracker.Core;
using TimeTracker.Core.Events;
using TimeTracker.Infrastructure;

namespace TimeTracker.Core.Tests;

public sealed class FileStoresTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TimeTrackerTests", Guid.NewGuid().ToString("N"));

    private string EventsPath => Path.Combine(_directory, "events.jsonl");

    private JsonLinesEventStore NewStore() => new(EventsPath, NullLogger<JsonLinesEventStore>.Instance);

    private static StateChanged Change() =>
        new(Guid.NewGuid(), T0, T0.AddMinutes(1), TimerState.Stop, TimerState.Active, TransitionReason.UserStart);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Missing_journal_reads_as_empty() => Assert.Empty(NewStore().ReadAll());

    [Fact]
    public void Events_round_trip_through_new_store_instance()
    {
        var change = Change();
        var adjustment = new TimeAdjusted(Guid.NewGuid(), T0, new DateOnly(2026, 3, 10), -5, "обед");

        var store = NewStore();
        store.Append(change);
        store.Append(adjustment);

        Assert.Equal<TrackerEvent>([change, adjustment], NewStore().ReadAll());
    }

    [Fact]
    public void Journal_has_one_readable_line_per_event()
    {
        var store = NewStore();
        store.Append(Change());
        store.Append(new TimeAdjusted(Guid.NewGuid(), T0, new DateOnly(2026, 3, 10), 5, "обед"));

        var lines = File.ReadAllLines(EventsPath);

        Assert.Equal(2, lines.Length);
        Assert.Contains("\"To\":\"Active\"", lines[0]);
        Assert.Contains("обед", lines[1]);
    }

    [Fact]
    public void Torn_last_line_is_skipped_and_does_not_corrupt_next_event()
    {
        var first = Change();
        NewStore().Append(first);
        File.AppendAllText(EventsPath, "{\"type\":\"state\",\"Id\":\"oborvano");

        var store = NewStore();
        Assert.Equal<TrackerEvent>([first], store.ReadAll());

        var second = Change();
        store.Append(second);

        Assert.Equal<TrackerEvent>([first, second], NewStore().ReadAll());
    }

    [Fact]
    public void Heartbeat_round_trips_and_overwrites()
    {
        var store = new FileHeartbeatStore(Path.Combine(_directory, "heartbeat.txt"));
        Assert.Null(store.Read());

        store.Write(T0);
        store.Write(T0.AddSeconds(30));

        Assert.Equal(T0.AddSeconds(30), store.Read());
    }

    [Fact]
    public void Corrupt_heartbeat_reads_as_null()
    {
        var path = Path.Combine(_directory, "heartbeat.txt");
        var store = new FileHeartbeatStore(path);
        File.WriteAllText(path, "мусор");

        Assert.Null(store.Read());
    }
}
