namespace TimeTracker.Core;

public sealed class TrackerOptions
{
    public TimeSpan IdleThreshold { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Каталог данных. Если не задан, используется %LOCALAPPDATA%\TimeTrackerSimple.</summary>
    public string? DataDirectory { get; set; }
}
