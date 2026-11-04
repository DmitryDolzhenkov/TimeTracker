using TimeTracker.Core.Events;

namespace TimeTracker.Core.Abstractions;

public interface IActivityMonitor
{
    /// <summary>Нет ввода дольше порога. Аргумент — момент последнего ввода.</summary>
    event Action<DateTimeOffset>? IdleDetected;
    event Action? InputResumed;
}

public interface ISessionEventsSource
{
    event Action? Locked;
    event Action? Unlocked;
    event Action? Ending;
}

public interface IEventStore
{
    /// <summary>Дописывает событие и сбрасывает его на диск до возврата управления.</summary>
    void Append(TrackerEvent trackerEvent);
    IReadOnlyList<TrackerEvent> ReadAll();
}

public interface IHeartbeatStore
{
    void Write(DateTimeOffset at);
    DateTimeOffset? Read();
}
